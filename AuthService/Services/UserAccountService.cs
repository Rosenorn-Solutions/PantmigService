using AuthService.Entities;
using AuthService.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AuthService.Data; // added for ApplicationDbContext

namespace AuthService.Services
{
    public interface IUserAccountService
    {
        Task<ChangePasswordResult> ChangePasswordAsync(string userId, string oldPassword, string newPassword, CancellationToken ct = default);
        Task<ChangeEmailResult> RequestEmailChangeAsync(string userId, string newEmail, string currentPassword, CancellationToken ct = default);
        Task<OperationResult> DisableAccountAsync(string userId, string currentPassword, string? reason, CancellationToken ct = default);
        Task<OperationResult> ConfirmEmailChangeAsync(string userId, string newEmail, string token, CancellationToken ct = default);
    }

    public sealed class UserAccountService : IUserAccountService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ITokenService _tokenService;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;
        private readonly ApplicationDbContext _db;
        public UserAccountService(UserManager<ApplicationUser> userManager, ITokenService tokenService, IEmailSender emailSender, IConfiguration config, ApplicationDbContext db)
        {
            _userManager = userManager;
            _tokenService = tokenService;
            _emailSender = emailSender;
            _config = config;
            _db = db;
        }
        public async Task<ChangePasswordResult> ChangePasswordAsync(string userId, string oldPassword, string newPassword, CancellationToken ct = default)
        {
            var result = new ChangePasswordResult();
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) { result.Success = false; result.ErrorMessage = "User not found"; return result; }
            if (user.IsDisabled) { result.Success = false; result.ErrorMessage = "Account disabled"; return result; }
            if (string.IsNullOrWhiteSpace(oldPassword) || string.IsNullOrWhiteSpace(newPassword)) { result.Success = false; result.ErrorMessage = "Passwords required"; return result; }
            if (string.Equals(oldPassword, newPassword, StringComparison.Ordinal)) { result.Success = false; result.ErrorMessage = "New password must differ"; return result; }
            var change = await _userManager.ChangePasswordAsync(user, oldPassword, newPassword);
            if (!change.Succeeded) { result.Success = false; result.ErrorMessage = string.Join("; ", change.Errors.Select(e => e.Description)); return result; }
            // Rotate tokens: revoke existing refresh tokens and issue new pair
            await RevokeAllRefreshTokens(user.Id, ct);
            var (access, exp) = _tokenService.GenerateAccessToken(user);
            var refresh = await _tokenService.GenerateAndStoreRefreshTokenAsync(user, ct);
            result.Success = true;
            result.AuthResponse = new AuthResponse { UserId = user.Id, Email = user.Email ?? string.Empty, UserName = user.UserName ?? string.Empty, FirstName = user.FirstName, LastName = user.LastName, AccessToken = access, AccessTokenExpiration = exp, RefreshToken = refresh, UserType = user.UserType, IsOrganization = user.IsOrganization, CityExternalId = user.City?.ExternalId, CityName = user.City?.Name, Gender = user.Gender, BirthDate = user.BirthDate };
            return result;
        }
        public async Task<ChangeEmailResult> RequestEmailChangeAsync(string userId, string newEmail, string currentPassword, CancellationToken ct = default)
        {
            var res = new ChangeEmailResult();
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) { res.Success = false; res.ErrorMessage = "User not found"; return res; }
            if (user.IsDisabled) { res.Success = false; res.ErrorMessage = "Account disabled"; return res; }
            if (string.IsNullOrWhiteSpace(newEmail)) { res.Success = false; res.ErrorMessage = "New email required"; return res; }
            var normalized = _userManager.NormalizeEmail(newEmail.Trim());
            var exists = await _userManager.Users.AnyAsync(u => u.NormalizedEmail == normalized, ct);
            if (exists) { res.Success = false; res.ErrorMessage = "Email already in use"; return res; }
            var pwdOk = await _userManager.CheckPasswordAsync(user, currentPassword);
            if (!pwdOk) { res.Success = false; res.ErrorMessage = "Invalid password"; return res; }
            var token = await _userManager.GenerateChangeEmailTokenAsync(user, newEmail.Trim());
            var tokenBytes = System.Text.Encoding.UTF8.GetBytes(token);
            var tokenEnc = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(tokenBytes);
            var baseUrl = _config["Domain"] ?? "https://auth.pantmig.dk";
            var confirmUrl = $"{baseUrl.TrimEnd('/')}/auth/confirm-email-change?userId={Uri.EscapeDataString(user.Id)}&email={Uri.EscapeDataString(newEmail.Trim())}&token={tokenEnc}";
            try
            {
                await _emailSender.SendAsync(newEmail.Trim(), "Bekræft din nye e-mail", "Klik for at bekræfte din e-mail ændring:\n" + confirmUrl, ct);
            }
            catch { }
            res.Success = true; res.RequiresConfirmation = true; return res;
        }
        public async Task<OperationResult> DisableAccountAsync(string userId, string currentPassword, string? reason, CancellationToken ct = default)
        {
            var res = new OperationResult();
            var user = await _userManager.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) { res.Success = false; res.ErrorMessage = "User not found"; return res; }
            if (user.IsDisabled) { res.Success = true; return res; }
            var pwdOk = await _userManager.CheckPasswordAsync(user, currentPassword);
            if (!pwdOk) { res.Success = false; res.ErrorMessage = "Invalid password"; return res; }
            user.IsDisabled = true; // optionally also set lockout
            await _userManager.UpdateAsync(user);
            await RevokeAllRefreshTokens(user.Id, ct);
            res.Success = true; return res;
        }
        public async Task<OperationResult> ConfirmEmailChangeAsync(string userId, string newEmail, string token, CancellationToken ct = default)
        {
            var res = new OperationResult();
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(newEmail) || string.IsNullOrWhiteSpace(token)) { res.Success = false; res.ErrorMessage = "Missing parameters"; return res; }
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) { res.Success = false; res.ErrorMessage = "Invalid user"; return res; }
            if (user.IsDisabled) { res.Success = false; res.ErrorMessage = "Account disabled"; return res; }
            try
            {
                var tokenBytes = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(token);
                var decodedToken = System.Text.Encoding.UTF8.GetString(tokenBytes);
                var changeRes = await _userManager.ChangeEmailAsync(user, newEmail.Trim(), decodedToken);
                if (!changeRes.Succeeded) { res.Success = false; res.ErrorMessage = string.Join("; ", changeRes.Errors.Select(e => e.Description)); return res; }
                // After successful change email becomes unconfirmed; require normal email confirmation again
                user.EmailConfirmed = false; await _userManager.UpdateAsync(user);
                res.Success = true; return res;
            }
            catch (Exception ex) { res.Success = false; res.ErrorMessage = ex.Message; return res; }
        }
        private async Task RevokeAllRefreshTokens(string userId, CancellationToken ct)
        {
            var tokens = await _db.RefreshTokens.Where(t => t.UserId == userId && t.Revoked == null && t.Expires > DateTime.UtcNow).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var t in tokens) { t.Revoked = now; }
            await _db.SaveChangesAsync(ct);
        }
    }
}
