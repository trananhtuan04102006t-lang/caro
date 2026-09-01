using Xunit;
using CaroNet.Server.Host.Validation;

namespace CaroNet.Server.Host.Tests
{
    public class PlayerNameSanitizerTests
    {
        [Theory]
        [InlineData("Chương", "Chương")] // Tên bình thường tiếng Việt
        [InlineData("Nguyễn Văn A", "Nguyễn Văn A")] // Tên tiếng Việt đầy đủ khoảng trắng
        [InlineData("Player_1-test", "Player_1-test")] // Gạch dưới và gạch ngang hợp lệ
        [InlineData("  Chương  ", "Chương")] // Trim khoảng trắng thừa ở đầu và cuối
        public void Sanitize_ShouldKeepSafeCharacters(string input, string expected)
        {
            var result = PlayerNameSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("<script>alert(1)</script>", "scriptalert1script")] // XSS script tag
        [InlineData("<img onerror=alert(1)>", "img onerroralert1")] // HTML img tag with error handler
        [InlineData("<svg onload=alert(1)>", "svg onloadalert1")] // SVG onload
        [InlineData("A&B\"C'D", "ABCD")] // Ký tự đặc biệt & " '
        [InlineData("Hello? World!", "Hello World")] // Ký tự ? và !
        public void Sanitize_ShouldStripUnsafeCharacters(string input, string expected)
        {
            var result = PlayerNameSanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("<>&\"")] // Chỉ chứa ký tự đặc biệt bị loại bỏ hoàn toàn
        public void Sanitize_ShouldReturnDefaultName_WhenInputIsInvalidOrEmpty(string? input)
        {
            var result = PlayerNameSanitizer.Sanitize(input);
            Assert.Equal("Player", result);
        }

        [Fact]
        public void Sanitize_ShouldLimitLengthTo20Characters()
        {
            // Một chuỗi 50 ký tự 'A'
            string longInput = new string('A', 50);
            
            var result = PlayerNameSanitizer.Sanitize(longInput);
            
            Assert.Equal(20, result.Length);
            Assert.Equal(new string('A', 20), result);
        }

        [Fact]
        public void Sanitize_ShouldTrimTrailingWhitespace_AfterTruncation()
        {
            // 19 ký tự 'A' + 1 khoảng trắng + 10 ký tự 'B'
            string input = new string('A', 19) + " " + new string('B', 10);
            
            var result = PlayerNameSanitizer.Sanitize(input);
            
            // Cắt còn 20 ký tự: 19 'A' + 1 khoảng trắng -> Trim thành 19 'A'
            Assert.Equal(19, result.Length);
            Assert.Equal(new string('A', 19), result);
        }
    }
}
