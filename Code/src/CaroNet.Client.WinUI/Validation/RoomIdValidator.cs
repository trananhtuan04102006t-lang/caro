using System;
using System.Collections.Generic;
using System.Text;

namespace CaroNet.Client.WinUI.Validation
{
    public static class RoomIdValidator
    {
        public static string? Validate(string roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                return "Vui lòng nhập mã phòng.";
            }

            if (roomId.Length != 6)
            {
                return "Mã phòng phải có đúng 6 ký tự.";
            }
            return null;
        }
    }
}
