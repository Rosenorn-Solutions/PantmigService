using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AuthService.Utils
{
    public static class SlugHelper
    {
        public static string ToSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var s = input.Trim().ToLowerInvariant();

            // Map Danish letters explicitly
            s = s
                .Replace("å", "a")
                .Replace("ø", "o")
                .Replace("æ", "ae");

            // Remove diacritics
            var normalized = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var ch in normalized)
            {
                var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (uc != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(ch);
                }
            }
            s = sb.ToString().Normalize(NormalizationForm.FormC);

            // Replace whitespace with hyphen
            s = Regex.Replace(s, "\\s+", "-");
            // Remove invalid chars
            s = Regex.Replace(s, "[^a-z0-9-]", "");
            // Collapse dashes
            s = Regex.Replace(s, "-+", "-");
            // Trim dashes
            s = s.Trim('-');
            return s;
        }
    }
}
