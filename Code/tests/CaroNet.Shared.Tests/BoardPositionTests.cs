using CaroNet.Shared.Game;

namespace CaroNet.Shared.Tests;

public class BoardPositionTests
{
    [Fact]
    public void BoardPosition_keeps_row_and_column_values()
    {
        var position = new BoardPosition(7, 9);

        Assert.Equal(7, position.Row);
        Assert.Equal(9, position.Column);
    }
}
