using System;
using System.Text.RegularExpressions;

namespace CaroNet.Server.Host.Validation
{
    public static class PlayerNameSanitizer
    {
        private static readonly Regex SafeCharactersRegex = new Regex(@"[^\p{L}\p{N}\s_\-]", RegexOptions.Compiled);

        public static string Sanitize(string? rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Player";
            }

            // 1. Loại bỏ các ký tự đặc biệt, tag HTML/XAML, chỉ giữ lại chữ cái, số, khoảng trắng, gạch dưới và gạch ngang
            string sanitized = SafeCharactersRegex.Replace(rawName, string.Empty);

            // 2. Trim khoảng trắng thừa
            sanitized = sanitized.Trim();

            // 3. Nếu sau khi lọc ra chuỗi rỗng thì trả về giá trị mặc định
            if (string.IsNullOrWhiteSpace(sanitized))
            {
                return "Player";
            }

            // 4. Giới hạn độ dài tối đa 20 ký tự để tránh tràn giao diện UI
            if (sanitized.Length > 20)
            {
                sanitized = sanitized.Substring(0, 20).TrimEnd();
            }

            return sanitized;
        }
    }
}
