# Đề Xuất Các Phương Án Triển Khai Hệ Thống E-Commerce Microservices (.NET 10)

Tài liệu này chi tiết hóa các phương án triển khai dựa trên [PROJECT_PLAN.md](PROJECT_PLAN.md). Mục tiêu nhằm phục vụ việc nghiên cứu, thử nghiệm kiến trúc phân tán (Microservices), đo lường hiệu năng và minh họa các anti-pattern trong thực tế.

---

## 1. Hiện trạng & Đánh giá kỹ thuật

### 1.1. Môi trường phát triển đã xác minh
* **Runtime:** .NET 10 SDK (`10.0.303`).
* **Container:** Docker Engine (`29.8.0`).
* **Version Control:** Git (`2.55.0`).
* **Mã nguồn:** Đã chọn Phương án 1 và hoàn thành Giai đoạn 1 (Git repo, `Ecommerce.slnx`, BuildingBlocks, Catalog, Basket, ApiGateway, `docker-compose.infra.yml`). Chi tiết hiện trạng: [README.md](README.md).

### 1.2. Các quyết định kiến trúc cốt lõi (Technical Architecture Decisions)
1. **Saga Pattern:** Sử dụng **MassTransit State Machine (Saga Orchestration)** tập trung tại `Order Service` thay vì Choreography. Cách tiếp cận này giúp quản lý trạng thái tập trung, trực quan hóa luồng nghiệp vụ và kiểm soát các compensating actions (hoàn trả kho, hủy giao dịch) an toàn.
2. **Chiến lược Authentication:**
   * *Giai đoạn đầu:* Sử dụng ASP.NET Core JWT Bearer nội bộ với mock tokens để tập trung xây dựng logic nghiệp vụ và giao tiếp liên service mà không tốn tài nguyên.
   * *Giai đoạn hoàn thiện:* Kích hoạt Keycloak container qua Docker Compose profile để kiểm thử luồng OIDC và Gateway token exchange.
3. **Quản lý hạ tầng nội bộ:** Ưu tiên `docker-compose.infra.yml` cho các dependency chính (PostgreSQL, Redis, RabbitMQ) kết hợp k6 load testing để đảm bảo tính ổn định và kiểm soát tài nguyên trên môi trường Windows.

---

## 2. Chi tiết các phương án lựa chọn

```
                        ┌────────────────────────────────────────────────────────┐
                        │              LỰA CHỌN PHƯƠNG ÁN TRIỂN KHAI             │
                        └────────────────────────────────────────────────────────┘
                                                    │
                 ┌──────────────────────────────────┼──────────────────────────────────┐
                 ▼                                  ▼                                  ▼
   ┌───────────────────────────┐      ┌───────────────────────────┐      ┌───────────────────────────┐
   │        PHƯƠNG ÁN 1        │      │        PHƯƠNG ÁN 2        │      │        PHƯƠNG ÁN 3        │
   │  Phased Milestones (Ưu)   │      │        Lean MVP           │      │     Full Scaffolding      │
   ├───────────────────────────┤      ├───────────────────────────┤      ├───────────────────────────┤
   │ • Chia 4 giai đoạn rõ ràng│      │ • Chỉ 3 core services     │      │ • Tạo 100% project ngay   │
   │ • Kiểm thử từng tầng      │      │ • Mock Payment/Inventory  │      │ • Dễ xung đột/nhiễu       │
   │ • Dễ kiểm soát rủi ro     │      │ • Tập trung demo nhanh    │      │ • Khó debug luồng ban đầu │
   └───────────────────────────┘      └───────────────────────────┘      └───────────────────────────┘
```

---

### PHƯƠNG ÁN 1: Triển khai theo giai đoạn (Phased Milestone Approach) — **Khuyên dùng**

Phương án này chia nhỏ quy trình phát triển thành các chặng độc lập. Mỗi chặng đều có thể chạy thử nghiệm và kiểm chứng trước khi chuyển sang chặng tiếp theo.

#### Giai đoạn 1: Nền tảng & Giao tiếp đồng bộ (Foundation & Sync Communication) — ✅ Đã xong
* Khởi tạo Git repository (`.gitignore`, `.editorconfig`, `.gitattributes`).
* Khởi tạo Solution .NET 10 (`Ecommerce.slnx`, `Directory.Build.props`, `Directory.Packages.props`):
  * `BuildingBlocks`: Exception handling (ProblemDetails), Logging (Serilog), health checks, pagination.
  * `BuildingBlocks.Contracts`: gRPC protos (integration events thêm ở Giai đoạn 2).
  * `src/ApiGateway`: YARP Reverse Proxy cấu hình routing và rate limiting.
  * `src/Services/Catalog`: CRUD sản phẩm/danh mục qua REST + gRPC Server endpoint.
  * `src/Services/Basket`: Quản lý giỏ hàng trên Redis + gRPC Client gọi Catalog để xác thực giá/tồn kho.
* Cấu hình `deploy/docker/docker-compose.infra.yml`: PostgreSQL (DB riêng cho Catalog), Redis (cổng host 6380).
* Ghi chú: Catalog/Basket ở giai đoạn này là một project `*.Api` duy nhất (rút gọn có chủ đích); cấu trúc Clean Architecture 4 project áp dụng từ Giai đoạn 2 — xem PROJECT_PLAN mục 3.1.

#### Giai đoạn 2: Xử lý bất đồng bộ & Giao dịch phân tán (Event-Driven & Saga)
* Tích hợp RabbitMQ và MassTransit vào `BuildingBlocks`.
* Xây dựng các service nghiệp vụ còn lại:
  * `src/Services/Order`: Quản lý đơn hàng, lưu trữ PostgreSQL.
  * `src/Services/Inventory`: Quản lý tồn kho, trừ kho tạm thời (hold stock).
  * `src/Services/Payment`: Mô phỏng thanh toán (thành công/thất bại).
  * `src/Services/Notification`: Consumer nhận event gửi email/log.
* Cài đặt **MassTransit Saga State Machine** và **Transactional Outbox Pattern** tại Order Service:
  * Thành công: `OrderCreated` → `StockReserved` → `PaymentCompleted` → `OrderConfirmed`.
  * Thất bại: `PaymentFailed` → `CompensateStock` (nhả kho) → `OrderCancelled`.

#### Giai đoạn 3: Giám sát, Khả năng chịu lỗi & Đo lường hiệu năng (Observability & Resilience)
* Triển khai OpenTelemetry phân tán (Traces, Metrics, Logs) đồng bộ sang Jaeger, Prometheus và Grafana Dashboard.
* Áp dụng resilience policies bằng `Microsoft.Extensions.Http.Resilience` (Polly): Timeout, Retry with Exponential Backoff, Circuit Breaker.
* Viết k6 scripts để đo baseline RPS, p95/p99 latency trước và sau tối ưu hóa.

#### Giai đoạn 4: Mở rộng Kubernetes & Minh họa Anti-Patterns (Scale & Pitfalls)
* Viết manifest Kubernetes: Deployment, Service, ConfigMap, Secrets, HPA (CPU/RAM) và KEDA (scale queue RabbitMQ về 0).
* Xây dựng runnable code demo cho 3 lỗi kiến trúc kinh điển:
  1. **Distributed Monolith:** Mô phỏng chuỗi gọi HTTP đồng bộ gây nghẽn dây chuyền vs giải pháp Event-driven.
  2. **Shared Database:** Mô phỏng sự cố schema migration vỡ chéo service vs Database-per-service.
  3. **Cascading Failure:** Mô phỏng service chết kéo sập toàn hệ thống do thiếu Circuit Breaker.
* Hoàn thiện tài liệu báo cáo: `architecture.md`, `anti-patterns.md`, `benchmark-report.md`.

* **Ưu điểm:** Cực kỳ an toàn, code sạch, dễ gỡ lỗi, bám sát các tiêu chuẩn của đồ án/nghiên cứu thực tế.
* **Nhược điểm:** Cần thời gian kiểm thử qua từng bước theo lộ trình.

---

### PHƯƠNG ÁN 2: MVP Tinh gọn (Lean Proof of Concept)

Tập trung hoàn thành một luồng chạy hoàn chỉnh end-to-end trong thời gian ngắn nhất, lược bỏ các thành phần râu ria.

* **Phạm vi triển khai:**
  * Chỉ xây dựng 3 service chính: `ApiGateway` (YARP), `Catalog Service`, và `Order Service`.
  * `Basket` được gộp hoặc giả lập; `Inventory` và `Payment` được mô phỏng dưới dạng in-memory stubs hoặc consumer nội bộ đơn giản.
  * Sử dụng Docker Compose tối giản (1 Postgres, 1 RabbitMQ).
* **Ưu điểm:** Có sản phẩm chạy demo trong vòng 1–2 ngày; kiểm chứng được ngay cơ chế Saga rollback.
* **Nhược điểm:** Thiếu tính thực tế của một hệ thống phân tán quy mô lớn; không thể hiện được sự độc lập hoàn toàn giữa 6 service như yêu cầu trong `PROJECT_PLAN.md`.

---

### PHƯƠNG ÁN 3: Khởi tạo toàn bộ khung (Full Scaffolding Upfront)

Khởi tạo toàn bộ các project và hạ tầng trong một lần duy nhất trước khi viết logic nghiệp vụ.

* **Phạm vi triển khai:**
  * Tạo đồng loạt tất cả các project trong solution (.NET 10): Gateway, 6 Services, BuildingBlocks, UnitTests.
  * Viết sẵn toàn bộ Dockerfile cho từng service và 1 file `docker-compose.yml` tổng lực.
  * Sau khi có bộ khung, lần lượt điền code xử lý nghiệp vụ vào từng service.
* **Ưu điểm:** Nhìn thấy ngay cấu trúc tổng thể của repository từ đầu.
* **Nhược điểm:** Dễ gặp lỗi phụ thuộc (dependency issues) chéo khi nhiều service chưa hoàn thiện nhưng đã liên kết; tốn nhiều thời gian cấu hình ban đầu trước khi thấy được bất kỳ tính năng nào hoạt động.

---

## 3. Ma trận so sánh tiêu chí

| Tiêu chí | Phương án 1 (Theo giai đoạn) | Phương án 2 (Lean MVP) | Phương án 3 (Scaffolding toàn bộ) |
|---|---|---|---|
| **Độ ổn định khi triển khai** | Rất cao (kiểm tra từng lớp) | Cao | Trung bình (dễ xung đột cấu hình) |
| **Phù hợp mục tiêu nghiên cứu/demo** | Tối ưu nhất (đầy đủ chỉ số & pattern) | Giới hạn (thiếu các service phụ) | Đầy đủ nhưng tốn công debug |
| **Khả năng kiểm soát tài nguyên RAM/CPU** | Tốt (bật dần theo nhu cầu) | Tốt | Nặng (chạy toàn bộ đồng thời) |
| **Thời gian có tính năng đầu tiên** | Nhanh (Catalog + Basket xong ngay) | Rất nhanh | Chậm (phải xong toàn bộ khung) |

---

## 4. Bảng duyệt & Đề xuất hành động

> [!TIP]
> **Khuyến nghị:** Duyệt **Phương án 1**. Đây là phương án tiêu chuẩn trong ngành phần mềm để xây dựng hệ thống phân tán không bị nợ kỹ thuật (technical debt).

### Kế hoạch hành động sau khi duyệt Phương án 1 (đã hoàn thành):
1. [x] `git init` và tạo file `.gitignore`, `.editorconfig`, `.gitattributes` cho môi trường .NET / C#.
2. [x] Tạo Solution `Ecommerce.slnx` (.NET 10).
3. [x] Tạo `src/BuildingBlocks/BuildingBlocks` (Exception → ProblemDetails, Logging, health checks, pagination) và `src/BuildingBlocks/BuildingBlocks.Contracts` (gRPC protos).
4. [x] Tạo file `deploy/docker/docker-compose.infra.yml` chạy PostgreSQL và Redis.
5. [x] Tạo `src/Services/Catalog/Catalog.Api` (REST CRUD + gRPC Server).
6. [x] Tạo `src/Services/Basket/Basket.Api` (Redis + gRPC Client).
7. [x] Tạo `src/ApiGateway` (YARP cấu hình reverse proxy + rate limiting).

**Tiếp theo:** Giai đoạn 2 — RabbitMQ + MassTransit, Order/Inventory/Payment/Notification, Saga + Outbox.
