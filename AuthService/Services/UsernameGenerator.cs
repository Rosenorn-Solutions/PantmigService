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

            // Transliterate to ASCII-only characters compatible with Identity's default AllowedUserNameCharacters
            static string TransliterateAscii(string input)
            {
                if (string.IsNullOrEmpty(input)) return string.Empty;

                // Map Danish letters explicitly
                input = input
                    .Replace("æ", "ae").Replace("Æ", "AE")
                    .Replace("ø", "o").Replace("Ø", "O")
                    .Replace("å", "a").Replace("Å", "A");

                var normalized = input.Normalize(NormalizationForm.FormD);
                var filtered = new StringBuilder();
                foreach (var ch in normalized)
                {
                    var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
                    if (cat == UnicodeCategory.NonSpacingMark)
                    {
                        continue; // skip combining marks
                    }

                    // Keep only ASCII letters/digits and allowed username punctuation
                    if ((ch <= 127) && (char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' || ch == '_' || ch == '@' || ch == '+'))
                    {
                        filtered.Append(ch);
                    }
                }
                return filtered.ToString();
            }

            static string ScrambleName(string? firstName, string? lastName)
            {
                var cleanedFirstName = TransliterateAscii(firstName ?? string.Empty);
                cleanedFirstName = new string([.. cleanedFirstName.Where(char.IsLetter)]);
                var cleanedLastName = TransliterateAscii(lastName ?? string.Empty);
                cleanedLastName = new string([.. cleanedLastName.Where(char.IsLetter)]);

                // Fallback if names do not contain letters after transliteration
                if (string.IsNullOrWhiteSpace(cleanedFirstName) && string.IsNullOrWhiteSpace(cleanedLastName))
                {
                    const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
                    var buf = new char[3];
                    for (int i = 0; i < buf.Length; i++) buf[i] = letters[RandomNumberGenerator.GetInt32(letters.Length)];
                    return new string(buf);
                }

                if (string.IsNullOrWhiteSpace(cleanedFirstName)) cleanedFirstName = "USER";
                if (string.IsNullOrWhiteSpace(cleanedLastName)) cleanedLastName = "NAME";

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
                    const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"; // ASCII only
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
                var prefix = TransliterateAscii(SelectRandomPrefix());
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
