using System;
using System.Collections.Generic;
using System.Text;

namespace CaroNet.Client.WinUI.Validation
{
    public static class PlayerNameValidator
    {
        public static string? Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) {
                return "Vui lòng nhập tên của bạn.";
            }

            if (name.Length <= 3)
            {
                return "Tên phải có ít nhất 4 ký tự.";
            }

            return null;
        }
    }
}
