using CaroNet.Storage.Users;

namespace CaroNet.Storage.Tests;

public sealed class SqliteUserAccountStoreTests
{
    [Fact]
    public async Task RegisterAsync_tao_user_moi_va_login_lai_duoc()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteUserAccountStore(database.Path);

        UserAccount registered = await store.RegisterAsync("alice", "1234", "Alice");
        UserAccount? loggedIn = await store.LoginAsync("ALICE", "1234");

        Assert.NotEqual(Guid.Empty, registered.UserId);
        Assert.NotNull(loggedIn);
        Assert.Equal(registered.UserId, loggedIn!.UserId);
        Assert.Equal("Alice", loggedIn.DisplayName);
    }

    [Fact]
    public async Task RegisterAsync_tu_choi_username_trung_khong_phan_biet_hoa_thuong()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteUserAccountStore(database.Path);

        await store.RegisterAsync("alice", "1234", "Alice");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RegisterAsync("ALICE", "1234", "Alice 2"));
    }

    [Fact]
    public async Task LoginAsync_tra_null_khi_sai_mat_khau()
    {
        using var database = TemporarySqliteDatabase.Create();
        var store = new SqliteUserAccountStore(database.Path);

        await store.RegisterAsync("alice", "1234", "Alice");

        UserAccount? loggedIn = await store.LoginAsync("alice", "wrong");

        Assert.Null(loggedIn);
    }
}
