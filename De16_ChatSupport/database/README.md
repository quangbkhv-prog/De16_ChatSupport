# Cơ sở dữ liệu

- Hệ quản trị: PostgreSQL 17, dữ liệu nằm ở Docker volume `pgdata` (không mất khi `docker compose down`, chỉ mất khi `down -v`).
- Schema do **EF Core migrations** (`backend/Shared/Migrations`) tạo tự động khi `chat-api` khởi động (`Database.MigrateAsync`). Bạn không cần chạy script thủ công.
- Dữ liệu mẫu: 2 nhân viên (`agent1`, `agent2`) và 2 phiên đã đóng kèm tin nhắn (tắt bằng `SEED_DEMO_DATA=false`).
- `schema-reference.sql`: DDL tham chiếu tương đương migration, chỉ để đọc/đối chiếu, **không** cần chạy.

## Bảng

| Bảng | Mục đích |
|---|---|
| `Participants` | Người tham gia: khách (theo từng phiên) và nhân viên (có `Username`, `PasswordHash` PBKDF2) |
| `Conversations` | Phiên chat: trạng thái, khách, nhân viên, thời điểm tạo/tiếp nhận/đóng, `LastSequence`, `MessageCount` |
| `Messages` | Tin nhắn đã được Worker lưu; khoá chính `Id` (MessageId) chống trùng; duy nhất `(ConversationId, Sequence)` |
| `OutboxEvents` | Sự kiện chờ phát vào RabbitMQ (độ tin cậy phía Chat Service) |
