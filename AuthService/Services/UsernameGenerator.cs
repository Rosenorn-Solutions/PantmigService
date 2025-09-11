using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AuthService.Entities;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Services
{
    public sealed class UsernameGenerator : IUsernameGenerator
    {
        private readonly IConfiguration _config;
        private readonly UserManager<ApplicationUser> _userManager;

        public UsernameGenerator(IConfiguration config, UserManager<ApplicationUser> userManager)
        {
            _config = config;
            _userManager = userManager;
        }

        public async Task<string> GenerateAsync(string? firstName, string? lastName, CancellationToken ct = default)
        {
            string[] prefixes = _config.GetSection("UsernameGenerators").Get<string[]>() ?? throw new InvalidOperationException("Missing username prefix configuration");

            string SelectRandomPrefix()
            {
                var index = RandomNumberGenerator.GetInt32(prefixes.Length);
                return prefixes[index];
            }

            static string TransliterateNordic(string input)
            {
                if (string.IsNullOrEmpty(input)) return string.Empty;
                var sb = new StringBuilder(input)
                    .Replace("Æ", "AE").Replace("æ", "ae")
                    .Replace("Ø", "OE").Replace("ø", "oe")
                    .Replace("Å", "AA").Replace("å", "aa");

                // Remove diacritics for the rest
                var normalized = sb.ToString().Normalize(NormalizationForm.FormD);
                var filtered = new StringBuilder();
                foreach (var ch in normalized)
                {
                    var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
                    if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                    {
                        filtered.Append(ch);
                    }
                }
                var noDiacritics = filtered.ToString().Normalize(NormalizationForm.FormC);

                var allowed = new StringBuilder(noDiacritics.Length);
                foreach (var ch in noDiacritics)
                {
                    if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' || ch == '_' || ch == '@' || ch == '+')
                    {
                        allowed.Append(ch);
                    }
                }
                return allowed.ToString();
            }

            static string ScrambleName(string? firstName, string? lastName)
            {
                var cleanedFirstName = TransliterateNordic(firstName ?? string.Empty);
                cleanedFirstName = new string([.. cleanedFirstName.Where(char.IsLetter)]);
                var cleanedLastName = TransliterateNordic(lastName ?? string.Empty);
                cleanedLastName = new string([.. cleanedLastName.Where(char.IsLetter)]);

                if (string.IsNullOrWhiteSpace(cleanedFirstName) || string.IsNullOrWhiteSpace(cleanedLastName)) 
                    throw new InvalidOperationException("Failed to generate Username");

                var chars = cleanedFirstName.ToUpperInvariant().ToCharArray().Concat(cleanedLastName.ToUpperInvariant().ToCharArray()).ToArray();

                for (int i = chars.Length - 1; i > 0; i--)
                {
                    int j = RandomNumberGenerator.GetInt32(i + 1);
                    (chars[i], chars[j]) = (chars[j], chars[i]);
                }
                var take = Math.Min(3, chars.Length);
                var fragment = new string([.. chars.Take(take)]);
                if (fragment.Length < 3)
                {
                    const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                    var sb = new StringBuilder(fragment);
                    while (sb.Length < 3)
                    {
                        sb.Append(letters[RandomNumberGenerator.GetInt32(letters.Length)]);
                    }
                    return sb.ToString();
                }
                return fragment;
            }

            string baseUsername()
            {
                var prefix = TransliterateNordic(SelectRandomPrefix());
                if (string.IsNullOrWhiteSpace(prefix)) prefix = "User";
                var scrambled = ScrambleName(firstName, lastName);
                return $"{prefix}-{scrambled}";
            }

            string candidate = baseUsername();
            string username = candidate;
            int counter = 0;
            while (true)
            {
                var exists = await _userManager.FindByNameAsync(username);
                if (exists is null) break;
                counter++;
                username = $"{candidate}-{counter}";
                if (counter > 1000)
                {
                    username = $"{candidate}-{Guid.NewGuid().ToString("N")[..4]}";
                    break;
                }
            }

            return username;
        }
    }
}
