using System.Text.Json;
using CaroNet.Shared.Protocol.Payloads;
using Xunit;

namespace CaroNet.Shared.Tests;

public sealed class PayloadSerializationTests
{
    [Fact]
    public void GameStatePayload_Serializes_With_CamelCase_PropertyNames()
    {
        var payload = new GameStatePayload
        {
            CurrentTurnPlayerId = "player-1",
            Board = new[]
            {
                new[] { "X", "", "" },
                new[] { "", "O", "" },
                new[] { "", "", "" }
            },
            IsGameOver = false,
            WinnerPlayerId = null
        };

        JsonElement element = JsonSerializer.SerializeToElement(payload);
        string json = element.GetRawText();

        // After [JsonPropertyName] fix, properties should be camelCase
        Assert.Contains("\"board\"", json);
        Assert.Contains("\"currentTurnPlayerId\"", json);
        Assert.Contains("\"isGameOver\"", json);
        Assert.Contains("\"winnerPlayerId\"", json);

        // Verify NOT PascalCase
        Assert.DoesNotContain("\"Board\"", json);
        Assert.DoesNotContain("\"CurrentTurnPlayerId\"", json);
    }

    [Fact]
    public void GameStatePayload_Board_Is_Readable_By_Client_TryGetProperty_Pattern()
    {
        var payload = new GameStatePayload
        {
            CurrentTurnPlayerId = "player-1",
            Board = new[]
            {
                new[] { "X", "", "" },
                new[] { "", "O", "" },
                new[] { "", "", "X" }
            }
        };

        JsonElement element = JsonSerializer.SerializeToElement(payload);

        // Simulate client's TryGetProperty with case-insensitive lookup
        bool boardFound = false;
        foreach (JsonProperty prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, "board", StringComparison.OrdinalIgnoreCase))
            {
                boardFound = true;
                Assert.Equal(JsonValueKind.Array, prop.Value.ValueKind);
                Assert.Equal(3, prop.Value.GetArrayLength());

                // Row 0
                var row0 = prop.Value[0];
                Assert.Equal("X", row0[0].GetString());
                Assert.Equal("", row0[1].GetString());

                // Row 1
                var row1 = prop.Value[1];
                Assert.Equal("O", row1[1].GetString());

                // Row 2
                var row2 = prop.Value[2];
                Assert.Equal("X", row2[2].GetString());
                break;
            }
        }

        Assert.True(boardFound, "Board property not found in serialized JSON");
    }

    [Fact]
    public void MakeMovePayload_Deserializes_From_CamelCase_Json()
    {
        // This simulates what the client sends (anonymous object with camelCase)
        var clientPayload = JsonSerializer.SerializeToElement(new
        {
            row = 7,
            column = 5,
            playerId = "player-123"
        });

        // This simulates what the server does
        var movePayload = clientPayload.Deserialize<MakeMovePayload>();

        Assert.NotNull(movePayload);
        Assert.Equal(7, movePayload!.Row);
        Assert.Equal(5, movePayload.Column);
        Assert.Equal("player-123", movePayload.PlayerId);
    }

    [Fact]
    public void HelloPayload_Deserializes_From_CamelCase_Json()
    {
        var clientPayload = JsonSerializer.SerializeToElement(new
        {
            playerName = "Alice"
        });

        var hello = clientPayload.Deserialize<HelloPayload>();

        Assert.NotNull(hello);
        Assert.Equal("Alice", hello!.PlayerName);
    }

    [Fact]
    public void JoinRoomPayload_Deserializes_From_CamelCase_Json()
    {
        var clientPayload = JsonSerializer.SerializeToElement(new
        {
            roomId = "123456"
        });

        var join = clientPayload.Deserialize<JoinRoomPayload>();

        Assert.NotNull(join);
        Assert.Equal("123456", join!.RoomId);
    }

    [Fact]
    public void TopRecordsReceivedPayload_Serializes_With_CamelCase_PropertyNames()
    {
        var payload = new TopRecordsReceivedPayload
        {
            Players =
            [
                new TopPlayerRecordPayload
                {
                    PlayerName = "Alice",
                    Wins = 3,
                    Losses = 1,
                    Draws = 0
                }
            ]
        };

        JsonElement element = JsonSerializer.SerializeToElement(payload);
        string json = element.GetRawText();

        Assert.Contains("\"players\"", json);
        Assert.Contains("\"playerName\"", json);
        Assert.Contains("\"wins\"", json);
        Assert.DoesNotContain("\"PlayerName\"", json);
    }
}
