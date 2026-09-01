using Xunit;
using System;
using CaroNet.Shared.Protocol.Payloads;

namespace CaroNet.Client.WinUI.Tests;

public class ChatPayloadTests
{
    [Fact]
    public void ChatReceivedPayload_Should_Hold_Correct_Data()
    {
        // 1. Arrange - Khởi tạo dữ liệu giả lập cho gói tin Chat
        var expectedSender = "Player1";
        var expectedMessage = "Hello Caro!";
        var expectedTime = DateTime.Now;

        // 2. Act - Tạo payload nhận về từ Server
        var payload = new ChatReceivedPayload
        {
            SenderName = expectedSender,
            Message = expectedMessage,
            Timestamp = expectedTime
        };

        // 3. Assert - Kiểm tra xem dữ liệu có được lưu và truyền đi chính xác không
        Assert.Equal(expectedSender, payload.SenderName);
        Assert.Equal(expectedMessage, payload.Message);
        Assert.Equal(expectedTime, payload.Timestamp);
    }
}