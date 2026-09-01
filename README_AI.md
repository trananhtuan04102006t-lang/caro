# CaroNet - Phát triển AI chơi Cờ Caro bằng Minimax và Alpha-Beta Pruning

## 1. Mục đích

Đây là phần phát triển thêm trên project **CaroNet** đã có sẵn. Project gốc là hệ thống chơi Cờ Caro giữa người với người qua client/server. Phần bổ sung này phục vụ môn **Trí tuệ nhân tạo**, tập trung xây dựng chế độ **Người đấu với máy (Human vs AI)**.

Thuật toán AI chính được sử dụng là:

- **Minimax** để tìm kiếm cây trạng thái trò chơi.
- **Alpha-Beta Pruning** để cắt các nhánh không cần thiết trong quá trình Minimax.
- **Heuristic Evaluation** để đánh giá thế cờ khi đạt độ sâu giới hạn.
- **Candidate Move Generation** để giảm số nước cần xét trên bàn 15x15.

Các thành phần trên là kỹ thuật hỗ trợ cho Minimax, không thay thế thuật toán Minimax + Alpha-Beta Pruning.

---

## 2. Phần nào được kế thừa từ project cũ?

Phần phát triển AI **không viết lại toàn bộ CaroNet**. Các thành phần chính của project cũ vẫn được giữ nguyên, đặc biệt:

- `CaroGameState`: quản lý trạng thái bàn cờ và lượt chơi.
- `CaroRuleEngine`: kiểm tra nước đi và điều kiện thắng.
- `GameViewModel`: điều khiển dữ liệu cho giao diện bàn cờ.
- `IGameClientService`: abstraction để UI không phụ thuộc trực tiếp vào cách triển khai game.
- `SocketGameClientService`: tiếp tục phục vụ chế độ người với người qua server.
- Các chức năng tài khoản, phòng chơi, lịch sử, ranking và storage của project cũ.

Chế độ AI được thêm độc lập để hạn chế ảnh hưởng đến chức năng PvP hiện tại.

---

## 3. Kiến trúc phần AI

```text
Người chơi
    |
    v
Đánh 1 nước
    |
    v
Board hiện tại
    |
    v
Candidate Move Generator
    |
    v
+--------------------------------+
|         Minimax Search         |
|                                |
|   MAX = AI       MIN = Human   |
|                                |
|   Alpha-Beta Pruning            |
+----------------+---------------+
                 |
                 v
         Evaluation Function
                 |
                 v
            Best Move
                 |
                 v
              AI đánh
```

Alpha-Beta được tích hợp trực tiếp trong quá trình Minimax search. Không phải chạy Minimax xong rồi mới chạy Alpha-Beta.

---

## 4. Các file được thêm

### Trong `CaroNet.Shared`

```text
Code/src/CaroNet.Shared/Game/AI/
├── AiDifficulty.cs
├── ICaroAiPlayer.cs
├── MinimaxAiPlayer.cs
├── SearchBoard.cs
├── CandidateMoveGenerator.cs
└── BoardEvaluator.cs
```

### Trong `CaroNet.Client.WinUI`

```text
Code/src/CaroNet.Client.WinUI/Services/
└── LocalAiGameClientService.cs
```

### Test

```text
Code/tests/CaroNet.Shared.Tests/
└── MinimaxAiPlayerTests.cs
```

---

## 5. Cách Minimax hoạt động

AI xem mỗi trạng thái bàn cờ như một node trong cây tìm kiếm.

- **MAX node**: lượt của AI, chọn giá trị lớn nhất.
- **MIN node**: lượt của người chơi, giả định đối thủ chọn phương án bất lợi nhất cho AI.
- Khi đạt `depth = 0`, trạng thái được đưa vào hàm đánh giá.

Công thức:

```text
MAX(s) = max(V(s'))
MIN(s) = min(V(s'))
```

Trong quá trình tìm kiếm, Alpha-Beta duy trì hai giá trị:

```text
alpha = giá trị tốt nhất MAX đã biết
beta  = giá trị tốt nhất MIN đã biết
```

Khi:

```text
alpha >= beta
```

nhánh hiện tại được cắt bỏ.

---

## 6. Vì sao cần Candidate Move Generator?

Bàn Caro là `15 x 15 = 225` ô. Nếu Minimax thử toàn bộ ô trống ở mỗi tầng, số node tăng rất nhanh.

Vì vậy AI chỉ sinh các nước gần khu vực đã có quân cờ, với bán kính mặc định `1-2` ô tùy độ khó.

```text
225 ô trên bàn
      |
      v
Candidate Move Generator
      |
      v
một tập nhỏ các nước đáng xét
      |
      v
Minimax + Alpha-Beta
```

Đây là tối ưu hóa để Minimax có thể chạy thực tế trên bàn 15x15.

---

## 7. Hàm đánh giá thế cờ

`BoardEvaluator` nhận biết các chuỗi quân và số đầu mở của chuỗi.

Các mẫu được ưu tiên gồm:

| Thế cờ | Điểm tương đối |
|---|---:|
| 5 quân liên tiếp | `10,000,000` |
| Open Four | `100,000` |
| Closed Four | `10,000` |
| Open Three | `5,000` |
| Closed Three | `500` |
| Open Two | `100` |
| Closed Two | `20` |

Điểm cuối cùng được tính theo hướng:

```text
Điểm = Điểm thế AI - Điểm thế đối thủ
```

Các giá trị này là heuristic và có thể tiếp tục tinh chỉnh trong quá trình thử nghiệm.

---

## 8. Các mức độ AI

```text
Easy
- depth = 1
- candidate limit = 12
- radius = 1

Medium
- depth = 2
- candidate limit = 18
- radius = 2

Hard
- depth = 3
- candidate limit = 24
- radius = 2

Expert
- depth = 4
- candidate limit = 28
- radius = 2
```

Đây là cấu hình khởi đầu. Có thể điều chỉnh sau khi benchmark trên máy chạy thực tế.

Ngoài Minimax, AI có hai bước chiến thuật trước khi tìm kiếm sâu:

1. Nếu AI có nước thắng ngay -> đánh ngay.
2. Nếu người chơi có nước thắng ngay -> chặn ngay.

Hai bước này giúp AI phản ứng đúng với các tình huống bắt buộc và giảm lượng tìm kiếm không cần thiết.

---

## 9. Tích hợp vào UI

Từ menu chính chọn:

```text
Người đấu với máy
```

Sau đó chọn:

```text
Dễ
Trung bình
Khó
Chuyên gia
```

Người chơi sử dụng `X`, AI sử dụng `O`.

Luồng chơi:

```text
Người chơi X
    |
    v
Đánh nước
    |
    v
Cập nhật GameState
    |
    v
AI O chạy Minimax + Alpha-Beta
    |
    v
AI đánh nước tốt nhất
    |
    v
Người chơi tiếp tục
```

AI chạy cục bộ thông qua `LocalAiGameClientService`. Chế độ PvP online vẫn sử dụng `SocketGameClientService` như project cũ.

---

## 10. Điểm thiết kế quan trọng

AI không sửa luật chơi và không tự quản lý một bộ luật Caro riêng.

Nước đi thực tế của AI vẫn được thực hiện bằng:

```csharp
CaroGameState.MakeMove(...)
```

Do đó:

```text
CaroRuleEngine
        ^
        |
CaroGameState
        ^
        |
LocalAiGameClientService
        ^
        |
MinimaxAiPlayer
```

AI chịu trách nhiệm **tìm nước đi**, còn `CaroGameState` và `CaroRuleEngine` tiếp tục chịu trách nhiệm **thực thi và kiểm tra luật chơi**.

---

## 11. Kiểm thử

Đã bổ sung test cho các tình huống cơ bản:

- AI có nước thắng ngay -> phải chọn nước thắng.
- Đối thủ có nước thắng ngay -> AI phải chặn.
- AI phải trả về một nước hợp lệ trên bàn đang chơi.

File:

```text
Code/tests/CaroNet.Shared.Tests/MinimaxAiPlayerTests.cs
```

Chạy test bằng Visual Studio / Rider hoặc lệnh:

```bash
dotnet test Code/CaroNet.slnx
```

Lưu ý: môi trường phát triển cần cài **.NET 10 SDK** và workload/SDK cần thiết cho WinUI của project.

---

## 12. Cách chạy project

1. Mở `Code/CaroNet.slnx` bằng Visual Studio có hỗ trợ .NET 10 + WinUI.
2. Restore NuGet packages.
3. Build solution.
4. Chạy server nếu muốn sử dụng các chức năng PvP online.
5. Chạy Client.
6. Đăng nhập tài khoản như project cũ.
7. Chọn **Người đấu với máy**.
8. Chọn độ khó.
9. Chơi và quan sát AI tìm nước bằng Minimax + Alpha-Beta Pruning.

---

## 13. Hướng phát triển tiếp theo

Nếu tiếp tục phát triển đề tài AI, các cải tiến phù hợp là:

```text
Minimax + Alpha-Beta
        |
        +-- Move Ordering
        |
        +-- Iterative Deepening
        |
        +-- Transposition Table
        |
        +-- Zobrist Hashing
        |
        +-- Threat Space Search
        |
        +-- Tinh chỉnh Heuristic
```

Các cải tiến trên vẫn có thể giữ **Minimax + Alpha-Beta Pruning là thuật toán trung tâm** của đề tài.

---

## 14. Phạm vi của phần phát triển

Phần này được xây dựng theo hướng:

> **Kế thừa hệ thống CaroNet có sẵn và phát triển thêm chức năng AI cho môn Trí tuệ nhân tạo.**

Không thay thế kiến trúc client/server cũ và không viết lại toàn bộ game engine. Mục tiêu là chứng minh việc áp dụng **Minimax + Alpha-Beta Pruning** vào một trò chơi Caro 15x15 thực tế, đồng thời tích hợp được AI vào giao diện hiện có.
