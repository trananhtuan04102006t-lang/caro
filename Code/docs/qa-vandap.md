# Tài liệu ôn tập vấn đáp môn Lập trình mạng - Dự án CaroNet

Tài liệu này được cập nhật theo code hiện tại trong `Code/src`, `Code/tests` và workflow `.github/workflows/dotnet.yml`, không dựa vào nội dung tài liệu cũ. Mục tiêu là giúp từng thành viên trả lời đúng phần mình phụ trách, nói được theo luồng thực tế của hệ thống và có ví dụ dễ nhớ khi bảo vệ.

## Cách học nhanh

- Nhớ đường đi chính: `WinUI` -> `ViewModel` -> `SocketGameClientService` -> `SocketClientConnection` -> TCP frame -> `GameMessageDispatcher` -> `RoomManager/GameRoom` -> `CaroNet.Storage`.
- Nhớ vai trò tổng quát: client chỉ gửi ý định, server giữ trạng thái thật, storage chỉ lưu dữ liệu đã được server xác nhận.
- Khi bí, hãy trả lời theo mẫu 3 bước: "Ai gửi?", "Ai quyết?", "Ai lưu/hiển thị?".

## Các điểm dễ nói nhầm

- Bàn cờ hiện tại là `15x15`, tức 225 ô, không phải `20x20`.
- Storage dùng `Microsoft.Data.Sqlite` trực tiếp, không dùng Dapper và không dùng Entity Framework.
- Lịch sử trận không nằm trong bảng `MatchHistory`; code dùng `Matches` và `MatchMoves`.
- Client không lưu username/password/display name ở local settings; chỉ lưu host/port server gần nhất.
- Client không tự vẽ nước đi ngay sau khi click; client chờ server broadcast `GameStateUpdated`.
- Timeout 30 giây có xử lý thật ở server trong `GameRoom`, còn countdown trong `GamePage` là phần hiển thị UI.

---

## 1. Nguyễn Trần Đình Chương (@Chouwzi)

**Mảng chính:** leader, kiến trúc tổng thể, CI/CD, hardening, auth, quick match, history/ranking integration.

**File nên nhớ:** `Program.cs`, `GameMessageDispatcher.cs`, `AppServices.cs`, `PasswordHasher.cs`, `.github/workflows/dotnet.yml`.

### Câu 1: Vì sao CaroNet chọn kiến trúc server authoritative?

- **Trả lời ngắn:** Server là nguồn sự thật của ván đấu. Client chỉ gửi request như `MakeMove`, `QuickMatch`, `Chat`; server kiểm tra đăng nhập, phòng, lượt, tọa độ, kết quả thắng/hòa rồi broadcast state đã xác nhận về hai client.
- **Mẹo nhớ:** "Client xin, server quyết, client vẽ."
- **Ví dụ nói nhanh:** Khi em bấm ô `(7, 8)`, client không tự kết luận mình đã đánh thành công. Nó gửi `MakeMove`; `GameMessageDispatcher` gọi `room.TryMakeMove`; nếu hợp lệ thì server gửi `GameStateUpdated`, lúc đó UI mới hiện quân cờ.

### Câu 2: Luồng đăng ký/đăng nhập hoạt động như thế nào và vì sao cần `UserId`?

- **Trả lời ngắn:** Client gửi `Register` hoặc `Login` với `AuthRequestPayload`. Server dùng `SqliteUserAccountStore` để tạo/kiểm tra tài khoản, sau đó lưu session vào `_authenticatedUsers` và trả `AuthAcceptedPayload` gồm `UserId`, `Username`, `DisplayName`. `UserId` giúp lịch sử cá nhân lấy đúng trận của tài khoản, không phụ thuộc tên hiển thị có thể trùng hoặc đổi.
- **Mẹo nhớ:** "Tên để hiển thị, `UserId` để định danh."
- **Ví dụ nói nhanh:** Hai người cùng tên "An" vẫn có `UserId` khác nhau, nên `GetMatchesByUserIdAsync` không lấy nhầm lịch sử.

### Câu 3: Password trong hệ thống được lưu an toàn hơn plain text như thế nào?

- **Trả lời ngắn:** `PasswordHasher` tạo salt ngẫu nhiên 16 byte, ghép salt với password rồi hash bằng SHA-256. Chuỗi lưu xuống DB có dạng `salt:hash` base64. Khi login, code hash lại password nhập vào và so sánh bằng `CryptographicOperations.FixedTimeEquals`.
- **Mẹo nhớ:** "Không lưu mật khẩu, chỉ lưu dấu vân tay có muối."
- **Ví dụ nói nhanh:** Hai người cùng đặt mật khẩu `1234` vẫn ra hash khác nhau vì salt khác nhau.

### Câu 4: Hệ thống chống spam request và frame quá lớn ở đâu?

- **Trả lời ngắn:** Server có rate limit trong `GameMessageDispatcher`: mỗi session không được gửi request liên tiếp dưới 100ms, trừ `Hello`, `Register`, `Login`. Frame quá lớn bị chặn bởi `ProtocolFrameCodec.MaxPayloadLength = 1 MB`; client cũng kiểm tra trong `SocketClientConnection.ReadFrameAsync`, server kiểm tra trong `ProtocolFrameReader`.
- **Mẹo nhớ:** "Chặn quá nhanh, chặn quá to."
- **Ví dụ nói nhanh:** Nếu client độc hại spam `MakeMove` liên tục, server trả `Rate limit exceeded`. Nếu frame khai báo dài 2 GB, client/server từ chối trước khi cấp phát mảng lớn.

### Câu 5: Vì sao `AppServices.FindDatabasePath()` không nên ném lỗi khi không tìm thấy database?

- **Trả lời ngắn:** `AppServices` có static property tạo service dùng chung. Nếu static initializer ném exception, WinUI có thể lỗi ngay lúc khởi động hoặc lúc class được dùng lần đầu. Code hiện tại tìm `src/CaroNet.Server.Host/caronet.db`; nếu không thấy thì fallback về `"caronet.db"` để app vẫn chạy.
- **Mẹo nhớ:** "Không tìm thấy DB thì đi đường phụ, không làm sập app."
- **Ví dụ nói nhanh:** Máy demo chưa có `caronet.db` trong thư mục server thì client vẫn mở được, sau đó các luồng network/history xử lý lỗi theo chức năng thay vì crash trắng màn hình.

### Câu 6: CI/CD của dự án đang kiểm tra những gì?

- **Trả lời ngắn:** Workflow `dotnet-build-test` chạy khi push hoặc pull request vào `develop`/`main`. Nó setup .NET `10.0.x`, restore `Code/CaroNet.slnx`, build Debug x64, test Debug x64 và upload file kết quả test `.trx`.
- **Mẹo nhớ:** "PR vào nhánh chính là phải restore, build, test."
- **Ví dụ nói nhanh:** Nếu sửa protocol làm test `ProtocolFrameCodecTests` fail, GitHub Actions sẽ báo đỏ trước khi merge.

---

## 2. Nguyễn Đức Thành (@NguyenDucThanh123)

**Mảng chính:** protocol, framing TCP, socket server infrastructure, disconnect, turn timer.

**File nên nhớ:** `MessageEnvelope.cs`, `MessageType.cs`, `ProtocolFrameCodec.cs`, `ProtocolFrameReader.cs`, `SocketServer.cs`, `ClientSession.cs`, `RoomManager.cs`, `GameRoom.cs`.

### Câu 1: Vì sao TCP cần length-prefix framing?

- **Trả lời ngắn:** TCP là byte stream, không giữ ranh giới message. Một lần `ReceiveAsync` có thể nhận nửa message hoặc nhiều message dính nhau. CaroNet thêm 4 byte đầu để ghi độ dài JSON payload, sau đó bên nhận đọc đúng số byte cần thiết.
- **Mẹo nhớ:** "TCP là dòng nước, length-prefix là vạch chia chai."
- **Ví dụ nói nhanh:** Nếu gửi `Login` rồi `QuickMatch`, server không đoán bằng dấu xuống dòng mà đọc 4 byte length trước mỗi JSON để tách đúng từng message.

### Câu 2: Frame trong CaroNet được encode/decode như thế nào?

- **Trả lời ngắn:** `ProtocolFrameCodec.Encode` serialize `MessageEnvelope` thành UTF-8 JSON, kiểm tra không quá 1 MB, tạo mảng `4 + payloadLength`, ghi length bằng `BinaryPrimitives.WriteInt32BigEndian`, rồi copy payload. `Decode` đọc length, kiểm tra mismatch, deserialize JSON và kiểm tra enum `MessageType` hợp lệ.
- **Mẹo nhớ:** "4 byte độ dài trước, JSON đi sau."
- **Ví dụ nói nhanh:** Payload dài 120 byte thì frame thật dài 124 byte: 4 byte đầu là số 120 theo big-endian, 120 byte sau là JSON.

### Câu 3: `ProtocolFrameReader` xử lý gói bị cắt hoặc bị gộp ra sao?

- **Trả lời ngắn:** Reader giữ một buffer `List<byte>`. Mỗi lần socket nhận dữ liệu, nó append vào buffer. Nếu chưa đủ 4 byte hoặc chưa đủ toàn bộ frame thì trả `false`. Khi đủ, nó cắt đúng frame ra và remove phần đã đọc khỏi buffer.
- **Mẹo nhớ:** "Chưa đủ thì để dành, đủ rồi mới bóc."
- **Ví dụ nói nhanh:** Nếu frame 500 byte nhưng lần đầu chỉ nhận 100 byte, reader không decode vội. Nó đợi các lần nhận sau cho đủ 500 byte.

### Câu 4: `ClientSession` và `SocketServer` phối hợp thế nào?

- **Trả lời ngắn:** `SocketServer` bind port `5000`, `Listen(100)`, liên tục `AcceptAsync`. Mỗi socket mới được bọc thành `ClientSession`, đưa vào `ClientSessionRegistry`, rồi chạy `session.RunAsync` trên task nền. Khi session kết thúc, server remove registry và gọi `GameMessageDispatcher.HandleDisconnectAsync`.
- **Mẹo nhớ:** "Server nhận cửa, session lo từng người."
- **Ví dụ nói nhanh:** Mỗi client có một `Guid Id`, một receive loop riêng và một send lock riêng.

### Câu 5: Vì sao `SendAsync` cần `SemaphoreSlim` và vòng lặp gửi hết dữ liệu?

- **Trả lời ngắn:** Nhiều task có thể cùng gửi message cho một socket, ví dụ vừa broadcast game state vừa gửi chat. `_sendLock` đảm bảo mỗi frame được gửi trọn vẹn, không bị xen byte với frame khác. Vòng lặp gửi vì `Socket.SendAsync` không bắt buộc gửi hết toàn bộ mảng trong một lần.
- **Mẹo nhớ:** "Một socket, một hàng gửi."
- **Ví dụ nói nhanh:** Frame dài 10 KB có thể chỉ gửi được 4 KB ở lần đầu; code tiếp tục gửi 6 KB còn lại.

### Câu 6: Server xử lý disconnect và timeout lượt đi như thế nào?

- **Trả lời ngắn:** Khi socket đóng, `SocketServer` gọi `HandleDisconnectAsync`; dispatcher xóa tên, auth, rate-limit state, rồi `RoomManager.HandleDisconnect` gỡ player khỏi phòng. Nếu ván đang chơi, người còn lại nhận `GameEnded` với reason `opponent_disconnected`. Timeout lượt nằm trong `GameRoom`: timer 30 giây gọi `HandleTurnTimeout`, người hết lượt thua và server broadcast `GameEnded` reason `timeout`.
- **Mẹo nhớ:** "Rớt mạng cũng là một kết thúc có kiểm soát."
- **Ví dụ nói nhanh:** Nếu đến lượt X mà X đứng yên 30 giây, server kết thúc ván cho O thắng, chứ client không tự quyết.

---

## 3. Nguyễn Trường Bảo (@Baong123)

**Mảng chính:** SQLite storage, lịch sử trận đấu, thống kê/ranking.

**File nên nhớ:** `DatabaseInitializer.cs`, `SqliteConnectionFactory.cs`, `SqliteMatchHistoryStore.cs`, `SqlitePlayerRecordStore.cs`, `MatchRecord.cs`, `MatchMoveRecord.cs`.

### Câu 1: SQLite database được khởi tạo ở đâu và gồm những bảng nào?

- **Trả lời ngắn:** Server khởi tạo database trong `Program.cs` bằng `DatabaseInitializer("caronet.db")`. Initializer bật `foreign_keys`, `journal_mode=WAL`, rồi tạo các bảng `Users`, `Matches`, `MatchMoves`, `PlayerRecords`. `Matches` lưu header trận, `MatchMoves` lưu từng nước đi, `PlayerRecords` lưu thắng/thua/hòa.
- **Mẹo nhớ:** "Users để đăng nhập, Matches để trận, MatchMoves để nước, PlayerRecords để rank."
- **Ví dụ nói nhanh:** Một trận có 20 nước sẽ có 1 dòng trong `Matches` và 20 dòng trong `MatchMoves`.

### Câu 2: Dự án dùng Dapper hay EF Core không?

- **Trả lời ngắn:** Không. Code hiện tại dùng `Microsoft.Data.Sqlite` trực tiếp: tạo `SqliteConnection`, `SqliteCommand`, add parameter như `$matchId`, `$playerName`, rồi gọi async command. Đây là cách gọn, rõ SQL và đủ cho đồ án.
- **Mẹo nhớ:** "Không ORM, SQL đi thẳng."
- **Ví dụ nói nhanh:** `SqliteMatchHistoryStore.SaveMoveAsync` tự viết câu `INSERT INTO MatchMoves`, không gọi `DbContext` hay Dapper.

### Câu 3: Lưu lịch sử trận đấu có transaction để làm gì?

- **Trả lời ngắn:** `SqliteMatchHistoryStore.SaveMatchAsync` mở transaction, xóa moves cũ của match nếu có, lưu header trận vào `Matches`, rồi lưu từng move vào `MatchMoves`. Chỉ khi tất cả thành công mới commit. Nhờ vậy không có trạng thái "trận đã lưu nhưng thiếu nước đi".
- **Mẹo nhớ:** "Header và moves đi cùng một chuyến xe."
- **Ví dụ nói nhanh:** Nếu đang lưu nước thứ 12 mà lỗi, transaction rollback, dữ liệu không bị nửa vời.

### Câu 4: Lịch sử cá nhân được lấy theo tên hay theo tài khoản?

- **Trả lời ngắn:** Theo `UserId`. Khi ván kết thúc, `SaveMatchHistoryAsync` gắn `PlayerXUserId`, `PlayerOUserId`, `WinnerUserId` vào `MatchRecord`. Khi client gửi `MyHistoryRequest`, server gọi `GetMatchesByUserIdAsync(user.UserId)`.
- **Mẹo nhớ:** "Lịch sử đi theo tài khoản, không đi theo nickname."
- **Ví dụ nói nhanh:** Nếu display name đổi từ "Bao" sang "Bảo", lịch sử vẫn tìm được vì `UserId` không đổi.

### Câu 5: Ranking Top 10 được tính và sắp xếp như thế nào?

- **Trả lời ngắn:** Sau mỗi ván, server cập nhật `PlayerRecords`: thắng tăng `Wins`, thua tăng `Losses`, hòa tăng `Draws`. `GetTopPlayersAsync(10)` sắp xếp theo win rate giảm dần, sau đó số trận thắng giảm dần, rồi tên tăng dần.
- **Mẹo nhớ:** "Tỉ lệ thắng trước, số thắng sau, tên để hòa giải."
- **Ví dụ nói nhanh:** Người A 8/10 thắng đứng trên người B 7/10 thắng; nếu cùng tỉ lệ thì ai nhiều wins hơn đứng trước.

### Câu 6: Code tránh lỗi ghi thống kê đồng thời ra sao?

- **Trả lời ngắn:** `GameMessageDispatcher` có `_playerRecordLocks` theo tên người chơi. Khi cập nhật một player, code lấy `SemaphoreSlim` tương ứng, đọc record hiện tại, cộng thắng/thua/hòa, rồi save lại. Việc này tránh hai luồng cùng đọc cùng một số cũ rồi ghi đè nhau.
- **Mẹo nhớ:** "Mỗi người chơi có một ổ khóa điểm."
- **Ví dụ nói nhanh:** Nếu hai trận của cùng một player kết thúc gần nhau, lock giúp hai lần cộng điểm không đạp lên nhau.

---

## 4. Nguyễn Hoàng Phúc (@phucnh8317-coder)

**Mảng chính:** client socket receive loop, chat, cấu hình client.

**File nên nhớ:** `SocketClientConnection.cs`, `SocketGameClientService.cs`, `GameViewModel.cs`, `MainMenuViewModel.cs`, `MainMenuPage.xaml.cs`, `GamePage.xaml.cs`.

### Câu 1: Receive loop phía client hoạt động như thế nào?

- **Trả lời ngắn:** Sau khi `ConnectAsync` thành công, `SocketClientConnection` tạo `_receiveLoopCts` và chạy `ReceiveLoopAsync` trên task nền. Loop đọc frame bằng `ReadFrameAsync`, decode thành `MessageEnvelope`, rồi phát event `MessageReceived` cho `SocketGameClientService`.
- **Mẹo nhớ:** "Socket nghe nền, service dịch message."
- **Ví dụ nói nhanh:** Khi server gửi `GameStateUpdated`, receive loop không cập nhật UI trực tiếp mà bắn event để service xử lý.

### Câu 2: Client đọc một frame TCP như thế nào để không bị thiếu dữ liệu?

- **Trả lời ngắn:** `ReadFrameAsync` gọi `ReadExactlyAsync(4)` để đọc length, kiểm tra length không âm và không quá 1 MB, rồi `ReadExactlyAsync(payloadLength)`. `ReadExactlyAsync` lặp `ReceiveAsync` đến khi đủ số byte.
- **Mẹo nhớ:** "Đọc đủ 4 byte trước, rồi đọc đủ thân sau."
- **Ví dụ nói nhanh:** Nếu payload 300 byte nhưng socket trả về 120 byte trước, code tiếp tục nhận thêm 180 byte.

### Câu 3: Vì sao phải marshal dữ liệu socket về UI thread?

- **Trả lời ngắn:** WinUI chỉ cho thay đổi UI trên UI thread. `GameViewModel` dùng `SafeExecuteOnUI`; `GamePage` truyền dispatcher bằng `DispatcherQueue.TryEnqueue`. Nhờ vậy event từ socket nền không sửa `ObservableCollection` hoặc property binding trực tiếp trên thread sai.
- **Mẹo nhớ:** "Socket ở hậu trường, UI ở sân khấu chính."
- **Ví dụ nói nhanh:** Tin chat đến từ receive loop sẽ được đưa qua dispatcher rồi mới add vào `ChatMessages`.

### Câu 4: Chat trong phòng đi qua những bước nào?

- **Trả lời ngắn:** UI gọi `GameViewModel.SendChatAsync`, service gửi `MessageType.Chat` với `ChatPayload`. Server kiểm tra đã đăng nhập, đang ở room, message không rỗng và không quá 200 ký tự. Sau đó server broadcast `ChatReceivedPayload` cho tất cả player trong phòng. UI dùng `SenderPlayerId` để phân biệt tin của mình, đối thủ và hệ thống.
- **Mẹo nhớ:** "Gửi một lần lên server, server phát lại cho cả phòng."
- **Ví dụ nói nhanh:** Người A gõ "xin chào", A cũng nhận lại `ChatReceived` từ server, nên giao diện của hai bên dùng cùng một nguồn dữ liệu.

### Câu 5: Client lưu cấu hình gì khi mở lại app?

- **Trả lời ngắn:** `MainMenuViewModel.ConnectAsync` chỉ lưu `ServerHost` và `ServerPort` vào `ApplicationData.Current.LocalSettings`. Nếu môi trường không hỗ trợ local settings, code fallback về file `%LocalAppData%/CaroNet/settings.txt`. `MainMenuPage.OnNavigatedTo` đọc lại các giá trị này.
- **Mẹo nhớ:** "Chỉ nhớ địa chỉ server, không nhớ tài khoản."
- **Ví dụ nói nhanh:** Để test nhiều client cùng máy, app không tự điền lại username/password/display name.

### Câu 6: Khi mất kết nối server, client dọn trạng thái ra sao?

- **Trả lời ngắn:** Nếu receive loop gặp lỗi hoặc socket đóng, `TryBeginDisconnect` đảm bảo dọn một lần, `CleanupConnection` dispose socket/CTS, rồi bắn `Disconnected`. `SocketGameClientService` xóa auth hiện tại, đặt status "Mất kết nối server", và cho các request đang chờ nhận exception.
- **Mẹo nhớ:** "Mất socket thì đóng cửa, báo người chờ."
- **Ví dụ nói nhanh:** Nếu đang chờ `QuickMatch` mà server tắt, task chờ room sẽ fail thay vì treo mãi.

---

## 5. Nguyễn Duy Tân (@tannd2333)

**Mảng chính:** UI menu, bàn cờ, turn indicator.

**File nên nhớ:** `GamePage.xaml`, `GamePage.xaml.cs`, `GameViewModel.cs`, `MainMenuPage.xaml.cs`, `MainMenuViewModel.cs`, `RoomIdValidator.cs`.

### Câu 1: Bàn cờ hiện tại được dựng như thế nào?

- **Trả lời ngắn:** Bàn cờ là `15x15`. `GameViewModel` tạo 225 `BoardCellViewModel` trong `ObservableCollection`. `GamePage.BuildBoard` tạo `RowDefinitions`, `ColumnDefinitions`, rồi thêm từng `Border` vào `BoardGrid`; bên trong có text quân cờ, overlay nước cuối và overlay đường thắng.
- **Mẹo nhớ:** "ViewModel giữ 225 ô, GamePage dựng 225 khung."
- **Ví dụ nói nhanh:** Ô `(row, column)` nằm tại index `row * 15 + column`.

### Câu 2: Turn indicator biết khi nào đến lượt mình bằng cách nào?

- **Trả lời ngắn:** Server gửi `currentTurnPlayerId`. `SocketGameClientService.ResolveCurrentTurnSymbol` so sánh id này với `_playerId`: nếu trùng thì current turn là `_playerSymbol`, nếu không thì là ký hiệu đối thủ. `GameViewModel.IsMyTurn` kiểm tra `CurrentTurnSymbol == PlayerSymbol`, có room, có đối thủ và ván chưa kết thúc.
- **Mẹo nhớ:** "Server gửi người đang đi, client đổi thành X/O để UI dễ vẽ."
- **Ví dụ nói nhanh:** Nếu em là O và server nói current turn là id của em, UI hiện "Lượt của bạn" và bật click ô trống.

### Câu 3: Khi người dùng bấm một ô cờ, UI xử lý ra sao?

- **Trả lời ngắn:** `BoardCell_Tapped` chỉ chạy nếu `IsMyTurn`, ván chưa kết thúc và ô đang trống. Sau đó gọi `GameViewModel.MakeMoveAsync`, service gửi `MakeMove` lên server. UI không tự đặt `Mark`; nó chờ `GameStateUpdated` rồi cập nhật board.
- **Mẹo nhớ:** "Click là đề nghị, broadcast mới là sự thật."
- **Ví dụ nói nhanh:** Nếu hai client click cùng lúc, server quyết nước nào hợp lệ theo lượt, client nào sai sẽ nhận `MoveRejected`.

### Câu 4: Countdown 30 giây trên UI khác gì timeout ở server?

- **Trả lời ngắn:** `GamePage` có `DispatcherTimer` để hiển thị số giây còn lại cho người chơi. Nó reset theo room, lượt hiện tại, last move và connection status. Nhưng kết quả timeout thật nằm ở `GameRoom` trên server; server mới quyết ai thua khi hết 30 giây.
- **Mẹo nhớ:** "UI đếm để người chơi thấy, server đếm để quyết định."
- **Ví dụ nói nhanh:** Nếu UI bị lag, server vẫn có timer riêng nên luật timeout không phụ thuộc vào máy client.

### Câu 5: Màn hình menu điều hướng các luồng chơi thế nào?

- **Trả lời ngắn:** `MainMenuPage` bind với `MainMenuViewModel`. Người dùng login/register trước, sau đó có thể `QuickMatch`, `CreateRoom`, `JoinRoom`. Khi thao tác thành công, page navigate sang `GamePage`. `RoomIdValidator` kiểm tra mã phòng có đúng 6 ký tự.
- **Mẹo nhớ:** "Menu là cổng vào: đăng nhập trước, chọn cách vào phòng sau."
- **Ví dụ nói nhanh:** Nếu chưa đăng nhập mà bấm chơi nhanh, ViewModel trả status "Bạn cần đăng nhập trước khi chơi nhanh."

### Câu 6: Vì sao constructor của `GamePage` phải gọi `InitializeComponent`, tạo ViewModel và set dispatcher?

- **Trả lời ngắn:** `InitializeComponent` dựng các control trong XAML. `DataContext = _viewModel` nối binding giữa UI và ViewModel. `SetDispatcher` giúp ViewModel cập nhật UI an toàn khi socket event chạy từ thread nền.
- **Mẹo nhớ:** "Dựng UI, nối dữ liệu, mở đường về UI thread."
- **Ví dụ nói nhanh:** Nếu quên `DataContext`, các binding như `TurnMessage`, `BoardCells`, `ChatMessages` sẽ không hiện đúng.

---

## 6. Trọng Nhân (@TrongNhan0510)

**Mảng chính:** rule engine, kết thúc ván, rematch, dialog kết quả, highlight thắng.

**File nên nhớ:** `CaroRuleEngine.cs`, `CaroGameState.cs`, `GameRoom.cs`, `GameEndedPayload.cs`, `SocketGameClientService.cs`, `GameViewModel.cs`, `GamePage.xaml.cs`.

### Câu 1: Thuật toán kiểm tra thắng hoạt động như thế nào?

- **Trả lời ngắn:** `CaroRuleEngine.GetWinningCells` bắt đầu từ nước cuối, lấy quân tại ô đó, rồi quét 4 hướng: ngang, dọc, chéo xuôi, chéo ngược. Mỗi hướng đếm liên tiếp cả chiều dương và chiều âm. Nếu tổng line có từ 5 ô trở lên thì thắng.
- **Mẹo nhớ:** "Một điểm cuối, bốn đường, hai chiều."
- **Ví dụ nói nhanh:** Với chéo xuôi `(1,1)`, code đếm từ ô cuối đi xuống phải và lên trái; đủ 5 quân cùng loại thì trả danh sách ô thắng.

### Câu 2: `CaroGameState.MakeMove` kiểm tra những điều kiện gì?

- **Trả lời ngắn:** Nó kiểm tra ván còn `Playing`, tọa độ trong bàn, ô còn trống, player có đúng lượt không. Nếu hợp lệ, nó đặt `X` hoặc `O`, tăng `MoveCount`, kiểm tra win, nếu chưa win thì kiểm tra draw, cuối cùng đổi `CurrentPlayer`.
- **Mẹo nhớ:** "Còn chơi, trong bàn, ô trống, đúng lượt."
- **Ví dụ nói nhanh:** Nếu O đánh khi `CurrentPlayer` là X, kết quả trả về `WrongTurn` và server gửi `MoveRejected`.

### Câu 3: Hòa được phát hiện khi nào?

- **Trả lời ngắn:** Sau một nước đi hợp lệ, nếu không có 5 quân liên tiếp và `MoveCount == Size * Size`, trạng thái chuyển thành `GameStatus.Draw`. Với size mặc định 15, bàn đầy là 225 nước.
- **Mẹo nhớ:** "Không ai đủ 5, bàn hết chỗ."
- **Ví dụ nói nhanh:** Nước thứ 225 mà không tạo đường 5 quân thì server broadcast `GameEnded` với winner null.

### Câu 4: Đầu hàng, xin hòa và timeout kết thúc ván qua luồng nào?

- **Trả lời ngắn:** `GameRoom` có các hàm `HandleResign`, `HandleDrawOffer`, `HandleDrawResponse`, `HandleTurnTimeout`. Dispatcher gọi các hàm này, sau đó broadcast `GameEnded` với reason tương ứng: `resigned`, `draw_agreed`, `timeout`. Khi ván kết thúc, server lưu lịch sử và cập nhật thống kê.
- **Mẹo nhớ:** "Room đổi trạng thái, dispatcher thông báo, storage ghi sổ."
- **Ví dụ nói nhanh:** Nếu người chơi bấm đầu hàng, `GameState.EndByResignation` đặt người còn lại thắng, rồi hai client nhận dialog kết quả.

### Câu 5: Rematch hoạt động như thế nào?

- **Trả lời ngắn:** Sau khi ván kết thúc, mỗi người bấm chơi lại sẽ được ghi vào `_rematchRequests`. Người đầu tiên tạo timer chờ 15 giây. Khi cả hai đồng ý, `ExecuteRematchReset` xóa move history, đổi người chơi X/O, reset board bằng `ResetForRematch`, rồi server gửi `RematchAccepted`.
- **Mẹo nhớ:** "Một người xin, hai người đồng ý, đổi phe chơi lại."
- **Ví dụ nói nhanh:** Nếu chỉ một người bấm chơi lại và đối thủ không bấm trong 15 giây, server gửi lỗi hết thời gian chờ.

### Câu 6: Highlight đường thắng và dialog kết quả lấy dữ liệu từ đâu?

- **Trả lời ngắn:** Khi thắng bằng nước đi cuối, `BroadcastGameEndedAsync(room, status, lastRow, lastCol, ...)` gọi `CaroRuleEngine.GetWinningCells` và đưa danh sách vào `GameEndedPayload.WinningCells`. Client đọc `winningCells`, lưu vào `SocketGameClientService.WinningCells`, `GameViewModel` đánh dấu `IsWinningCell`, rồi `GamePage` highlight overlay và hiện `ContentDialog`.
- **Mẹo nhớ:** "Server tìm đường thắng, client tô sáng."
- **Ví dụ nói nhanh:** Nếu X thắng ngang ở hàng 4, payload gửi về danh sách 5 ô; UI tô overlay xanh trên đúng 5 ô đó.

### Câu 7: Nếu viết test cho rule engine, nên test những trường hợp nào?

- **Trả lời ngắn:** Nên test đủ 4 hướng thắng, sai lượt, ô đã có quân, tọa độ ngoài bàn, draw và reset/rematch. Test nên tạo `CaroGameState`, gọi `MakeMove` theo thứ tự hợp lệ, rồi assert `GameStatus` và `MoveRejectReason`.
- **Mẹo nhớ:** "Test cả đường thắng lẫn đường từ chối."
- **Ví dụ nói nhanh:** Để test chéo ngược, tạo 5 quân cùng loại tại `(4,0)`, `(3,1)`, `(2,2)`, `(1,3)`, `(0,4)` bằng chuỗi nước đi hợp lệ, rồi assert trạng thái thắng.

---

## Câu tổng hợp nhóm

### Nếu giảng viên hỏi "Điểm mạng của đồ án nằm ở đâu?", trả lời sao?

- **Trả lời ngắn:** Điểm mạng nằm ở việc dùng raw TCP socket async, tự thiết kế JSON length-prefix protocol, quản lý session/phòng chơi trên server, đồng bộ trạng thái server authoritative cho hai client, xử lý disconnect, timeout, chat, rate limit và lưu lịch sử sau khi server xác nhận kết quả.
- **Mẹo nhớ:** "Không phải Caro offline gắn mạng, mà là ván cờ được server điều phối."
- **Ví dụ nói nhanh:** Cùng một nước đi đi qua TCP frame, được server kiểm tra trong `GameRoom`, rồi broadcast về cả hai client để hai màn hình đồng bộ.

### Nếu cần mô tả luồng một ván hoàn chỉnh trong 30 giây

1. Client kết nối port `5000`, gửi `Hello`, rồi `Login` hoặc `Register`.
2. Người chơi chọn `QuickMatch`, `CreateRoom` hoặc `JoinRoom`; server dùng `RoomManager` ghép vào `GameRoom`.
3. Khi đủ 2 người, server gửi `GameStarted`, bắt đầu timer 30 giây.
4. Mỗi nước đi là `MakeMove`; server kiểm tra lượt, ô trống, thắng/hòa rồi gửi `GameStateUpdated`.
5. Khi kết thúc do thắng, hòa, đầu hàng, timeout hoặc disconnect, server gửi `GameEnded`, lưu `Matches/MatchMoves`, cập nhật `PlayerRecords`.
6. Client hiển thị dialog, highlight đường thắng nếu có, cho phép chơi lại hoặc về menu.
