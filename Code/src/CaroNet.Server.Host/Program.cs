using CaroNet.Server.Host.GameRooms;
using CaroNet.Server.Host.Networking;
using CaroNet.Server.Host.Services;
using CaroNet.Storage.Database;
using CaroNet.Storage.Matches;
using CaroNet.Storage.Statistics;
using CaroNet.Storage.Users;

var cancellationTokenSource = new CancellationTokenSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    Console.WriteLine("[SERVER] Shutdown requested...");
    eventArgs.Cancel = true;
    cancellationTokenSource.Cancel();
};

const string dbPath = "caronet.db";
var dbInitializer = new DatabaseInitializer(dbPath);
dbInitializer.Initialize();
Console.WriteLine("[SERVER] Database initialized.");

var matchStore = new SqliteMatchHistoryStore(dbPath);
var playerRecordStore = new SqlitePlayerRecordStore(dbPath);
var userAccountStore = new SqliteUserAccountStore(dbPath);

var registry = new ClientSessionRegistry();
var roomManager = new RoomManager();

var dispatcher = new GameMessageDispatcher(
    roomManager,
    registry,
    matchStore,
    playerRecordStore,
    userAccountStore);

var server = new SocketServer(registry, dispatcher);

try
{
    await server.RunAsync(cancellationTokenSource.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("[SERVER] Stopped.");
}
