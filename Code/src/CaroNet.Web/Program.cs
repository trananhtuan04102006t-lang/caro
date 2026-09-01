using System.Text.Json.Serialization;
using CaroNet.Shared.Game;
using CaroNet.Shared.Game.AI;
using CaroNet.Web.Models;
using CaroNet.Web.Services;


var builder = WebApplication.CreateBuilder(args);


// Cho phép JSON nhận enum dạng chuỗi:
// "Easy", "Medium", "Hard", "Expert"
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
});


builder.Services.AddSingleton<CaroAiGameService>();


var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();


// ========================================
// HEALTH CHECK
// ========================================

app.MapGet("/api/health", () =>
{
    return Results.Ok(new
    {
        status = "ok",
        message = "CaroNet Web is running"
    });
});


// ========================================
// START GAME
// ========================================

app.MapPost("/api/game/start", (
    StartGameRequest request,
    CaroAiGameService gameService) =>
{
    var game = gameService.CreateGame(
        request.Difficulty);

    return Results.Ok(
        ToResponse(
            game,
            null,
            null));
});


// ========================================
// PLAYER MOVE
// ========================================

app.MapPost("/api/game/move", (
    MoveRequest request,
    CaroAiGameService gameService) =>
{
    var game = gameService.GetGame(
        request.GameId);

    if (game is null)
    {
        return Results.NotFound(new
        {
            message = "Không tìm thấy ván cờ."
        });
    }

    lock (game.SyncRoot)
    {
        // Phải tới lượt người chơi X
        if (game.State.CurrentPlayer !=
            game.HumanPlayer)
        {
            return Results.BadRequest(new
            {
                message =
                    "Chưa tới lượt người chơi."
            });
        }


        var playerMove =
            new BoardPosition(
                request.Row,
                request.Column);


        // Người chơi đánh X
        var playerResult =
            game.State.MakeMove(
                playerMove,
                game.HumanPlayer);


       if (!playerResult.IsSuccess)
        {
            return Results.BadRequest(new
            {
                message =
                    $"Nước đi không hợp lệ: {playerResult.Reason}"
            });
        }


        MoveDto? aiMove = null;


        // Nếu người chơi chưa thắng,
        // AI mới được phép suy nghĩ.
        if (game.State.Status ==
            GameStatus.Playing)
        {
            var bestMove =
                game.Ai.FindBestMove(
                    game.State,
                    game.AiPlayer);


            // AI đánh O
            var aiResult =
                game.State.MakeMove(
                    bestMove,
                    game.AiPlayer);


            if (aiResult.IsSuccess)
            {
                aiMove = new MoveDto(
                    bestMove.Row,
                    bestMove.Column);
            }
        }


        return Results.Ok(
            ToResponse(
                game,

                new MoveDto(
                    request.Row,
                    request.Column),

                aiMove));
    }
});


// ========================================
// GET GAME STATE
// ========================================

app.MapGet(
    "/api/game/{gameId:guid}",
    (
        Guid gameId,
        CaroAiGameService gameService) =>
{
    var game =
        gameService.GetGame(gameId);

    if (game is null)
    {
        return Results.NotFound(new
        {
            message =
                "Không tìm thấy ván cờ."
        });
    }

    lock (game.SyncRoot)
    {
        return Results.Ok(
            ToResponse(
                game,
                null,
                null));
    }
});


// ========================================
// RESET GAME
// ========================================

app.MapPost(
    "/api/game/{gameId:guid}/reset",
    (
        Guid gameId,
        CaroAiGameService gameService) =>
{
    var game =
        gameService.GetGame(gameId);

    if (game is null)
    {
        return Results.NotFound(new
        {
            message =
                "Không tìm thấy ván cờ."
        });
    }

    lock (game.SyncRoot)
    {
        game.State.Reset();

        return Results.Ok(
            ToResponse(
                game,
                null,
                null));
    }
});


// ========================================
// RUN
// ========================================

app.Run();


// ========================================
// CONVERT GAME STATE → JSON
// ========================================

static GameResponse ToResponse(
    GameSession game,
    MoveDto? playerMove,
    MoveDto? aiMove)
{
    var board =
        new string[game.State.Size];


    for (
        var row = 0;
        row < game.State.Size;
        row++)
    {
        var cells =
            new char[game.State.Size];


        for (
            var column = 0;
            column < game.State.Size;
            column++)
        {
            cells[column] =
                game.State[row, column] switch
                {
                    CellState.X => 'X',

                    CellState.O => 'O',

                    _ => '.'
                };
        }


        board[row] =
            new string(cells);
    }


    return new GameResponse(
        game.Id,
        game.State.Size,
        game.State.CurrentPlayer.ToString(),
        game.State.Status.ToString(),
        game.Ai.Difficulty.ToString(),
        board,
        playerMove,
        aiMove);
}