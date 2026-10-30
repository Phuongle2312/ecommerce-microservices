# Dự án Microservice E-commerce (.NET)

> Mục tiêu: xây dựng hệ thống mẫu để nghiên cứu và trình bày chủ đề **Microservice**: kiến trúc, ví dụ thực tế, scale up/down và hiệu năng tối ưu, các sai lầm làm hỏng kiến trúc.

**Nguyên tắc cốt lõi:** Microservice = tách hệ thống thành nhiều service nhỏ, độc lập.
**Trở ngại cần giải quyết:** phức tạp, độ trễ, tốn tài nguyên.

---

## 1. Công nghệ

| Thành phần | Công nghệ |
|---|---|
| Runtime / API | .NET 10 LTS, ASP.NET Core Minimal API |
| API Gateway | YARP |
| Đồng bộ | REST (client → gateway), gRPC (service ↔ service) |
| Bất đồng bộ | RabbitMQ + MassTransit |
| Database | PostgreSQL (mỗi service một DB), Redis (cache) |
| Dev orchestration | docker-compose (hạ tầng) + `dotnet run`; .NET Aspire dự kiến thêm sau |
| Triển khai | Docker, Kubernetes (HPA, KEDA) |
| Resilience | Microsoft.Extensions.Http.Resilience (Polly) |
| Observability | OpenTelemetry, Prometheus, Grafana, Jaeger, Serilog |
| Auth | Keycloak (JWT / OIDC) |
| CI/CD | GitHub Actions |
| Frontend | Next.js (App Router), TypeScript, Tailwind CSS, shadcn/ui |
| FE data / form | TanStack Query, React Hook Form + Zod, openapi-typescript (sinh client từ OpenAPI) |
| FE Auth | Auth.js (OIDC với Keycloak) |
| Load test | k6 |

---

## 2. Kiến trúc tổng quan

```
Client (Browser) ──► Web (Next.js: Storefront + Admin)
        │  HTTPS
        ▼
   API Gateway (YARP) ── Auth (Keycloak)
        │
 ┌──────┼───────────┬───────────┬───────────┐
 ▼      ▼           ▼           ▼           ▼
Catalog Basket     Order     Inventory   Payment ──► Notification
 (PG)   (Redis)    (PG)        (PG)       (PG)         (queue consumer)
        │ gRPC       │            ▲           ▲
        └──► Catalog │            └── RabbitMQ (events) ──┘
```

### Pattern áp dụng
- API Gateway, Database per Service
- Saga **orchestration** (MassTransit State Machine tại Order) cho luồng đặt hàng — xem [EXECUTION_OPTIONS.md](EXECUTION_OPTIONS.md)
- Outbox pattern (đảm bảo gửi event tin cậy)
- CQRS ở Catalog (read/write tách biệt) — dự kiến; giai đoạn 1 Catalog là CRUD trực tiếp qua EF Core
- Idempotent consumer, eventual consistency
- Circuit breaker, retry, timeout

### Danh sách service

| Service | Trách nhiệm | DB | Giao tiếp |
|---|---|---|---|
| Identity | Đăng nhập, phân quyền | Keycloak | OIDC |
| Catalog | Sản phẩm, danh mục | PostgreSQL | REST, gRPC server |
| Basket | Giỏ hàng | Redis | REST, gRPC client → Catalog |
| Order | Tạo và theo dõi đơn hàng, điều phối Saga | PostgreSQL | REST, publish/consume event |
| Inventory | Tồn kho, giữ hàng | PostgreSQL | Event |
| Payment | Thanh toán (giả lập) | PostgreSQL | Event |
| Notification | Gửi email/thông báo | - | Consume event |

### Luồng đặt hàng (Saga)
1. Order tạo đơn `Pending` → publish `OrderCreated`
2. Inventory giữ hàng → `StockReserved` (hoặc `StockFailed`)
3. Payment trừ tiền → `PaymentCompleted` (hoặc `PaymentFailed`)
4. Order cập nhật `Confirmed` / `Cancelled`
5. Lỗi ở bước nào thì chạy **compensating action** (nhả hàng, hoàn tiền)
6. Notification gửi kết quả cho khách

---

## 3. Cấu trúc thư mục

Cấu trúc đích (thư mục gốc là root của repo; hiện trạng xem [README.md](README.md)):

```
./
├── Ecommerce.slnx
├── src/
│   ├── ApiGateway/                # YARP
│   ├── Web/
│   │   ├── storefront/            # Next.js: trang bán hàng (SSR/ISR)
│   │   └── admin/                 # Next.js: trang quản trị
│   ├── Services/
│   │   ├── Catalog/
│   │   ├── Basket/
│   │   ├── Order/
│   │   ├── Inventory/
│   │   ├── Payment/
│   │   └── Notification/
│   ├── BuildingBlocks/
│   │   ├── BuildingBlocks/            # Logging, ProblemDetails, health checks, pagination, Resilience
│   │   └── BuildingBlocks.Contracts/  # gRPC protos, integration events
│   └── AppHost/                   # .NET Aspire (dự kiến)
├── deploy/
│   ├── docker/                    # docker-compose.infra.yml, Dockerfile, docker-compose.yml
│   └── k8s/                       # deployment, service, HPA, KEDA
├── tests/
│   ├── UnitTests/
│   ├── IntegrationTests/
│   └── LoadTests/                 # k6 scripts
├── docs/
│   ├── architecture.md
│   ├── anti-patterns.md
│   └── benchmark-report.md
└── .github/workflows/
```

Mỗi service theo cấu trúc: `Api / Application / Domain / Infrastructure`.

---

## 3.1. Cấu trúc theo SOLID (Clean Architecture trong từng service)

> **Hiện trạng (giai đoạn 1):** Catalog và Basket đang là một project duy nhất (`*.Api`) chia thư mục `Domain/`, `Infrastructure/`, `Endpoints/`; endpoint gọi thẳng `DbContext`/repository. Đây là bản rút gọn có chủ đích để có luồng chạy sớm. Cấu trúc 4 project dưới đây là **đích** — áp dụng cho Order/Inventory/Payment từ giai đoạn 2, và tách lại Catalog/Basket khi thêm architecture test.

Mỗi service là một "ứng dụng nhỏ" tự chứa, tổ chức theo 4 lớp. **Luồng phụ thuộc chỉ đi vào trong**: `Api → Application → Domain`, còn `Infrastructure` triển khai các interface do `Application`/`Domain` định nghĩa (Dependency Inversion).

```
        Api (Controller/Endpoint, gRPC, Consumer)
            │ gọi
            ▼
        Application (Use case, Command/Query, Interface/Port)
            │ dùng
            ▼
        Domain (Entity, Value Object, Domain Event, Rule)  ◄── không phụ thuộc gì
            ▲
            │ implement interface
        Infrastructure (EF Core, Redis, RabbitMQ, HTTP client)
```

### Cấu trúc chi tiết (ví dụ Order service)

```
Services/Order/
├── Order.Domain/                      # Lõi nghiệp vụ, KHÔNG tham chiếu project khác
│   ├── Entities/
│   │   ├── Order.cs                   # Aggregate root
│   │   └── OrderItem.cs
│   ├── ValueObjects/                  # Money, Address
│   ├── Events/                        # OrderCreated, OrderCancelled
│   ├── Enums/                         # OrderStatus
│   └── Exceptions/
│
├── Order.Application/                 # Use case. Tham chiếu: Domain
│   ├── Abstractions/                  # Interface (port) do tầng trong định nghĩa
│   │   ├── IOrderRepository.cs
│   │   ├── IUnitOfWork.cs
│   │   ├── IPaymentGateway.cs
│   │   └── IEventPublisher.cs
│   ├── Orders/
│   │   ├── Commands/CreateOrder/      # Command + Handler + Validator (1 file/1 use case)
│   │   ├── Commands/CancelOrder/
│   │   └── Queries/GetOrderById/
│   ├── Behaviors/                     # Validation, Logging, Transaction (pipeline)
│   └── DependencyInjection.cs
│
├── Order.Infrastructure/              # Chi tiết kỹ thuật. Tham chiếu: Application, Domain
│   ├── Persistence/
│   │   ├── OrderDbContext.cs
│   │   ├── Configurations/            # EF Fluent API
│   │   ├── Repositories/OrderRepository.cs
│   │   └── Migrations/
│   ├── Messaging/                     # MassTransit publisher, Outbox
│   ├── Clients/PaymentGrpcClient.cs
│   └── DependencyInjection.cs
│
└── Order.Api/                         # Điểm vào. Tham chiếu: Application, Infrastructure
    ├── Endpoints/OrderEndpoints.cs    # Minimal API, mỏng, chỉ map HTTP → command
    ├── Consumers/                     # Consumer event (StockReserved, PaymentCompleted)
    ├── Middleware/                    # Exception handling, correlation id
    └── Program.cs                     # Composition root: đăng ký DI
```

**Quy tắc tham chiếu project:**

| Project | Được tham chiếu |
|---|---|
| Domain | (không gì cả) |
| Application | Domain |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure (chỉ để đăng ký DI) |

> Nên thêm **architecture test** (NetArchTest) để CI tự fail nếu ai vi phạm các quy tắc trên.

### Áp dụng từng nguyên lý SOLID

| Nguyên lý | Áp dụng trong dự án | Dấu hiệu vi phạm |
|---|---|---|
| **S** – Single Responsibility | Mỗi use case một handler (`CreateOrderHandler`). Endpoint chỉ map HTTP, repository chỉ truy cập dữ liệu, domain chứa luật nghiệp vụ. Mỗi microservice một bounded context. | Controller chứa logic nghiệp vụ và gọi thẳng DbContext; service "God class" |
| **O** – Open/Closed | Thêm tính năng bằng cách thêm class mới: thêm phương thức thanh toán = thêm một `IPaymentProvider`; thêm validation/logging = thêm pipeline behavior, không sửa handler cũ. | Chuỗi `if/switch` theo loại, sửa code cũ mỗi lần thêm loại mới |
| **L** – Liskov Substitution | Mọi triển khai của `IPaymentProvider` (Momo, VNPay, Fake) thay thế được cho nhau mà không đổi hành vi hợp đồng; fake/in-memory repository dùng được trong test. | Implementation ném `NotImplementedException`, hoặc kiểm tra kiểu cụ thể (`is`/`as`) |
| **I** – Interface Segregation | Tách interface nhỏ theo nhu cầu: `IOrderReader` (query) và `IOrderWriter` (command) thay vì một `IOrderService` khổng lồ; contract event chỉ chứa dữ liệu consumer cần. | Interface 15+ method, class phải implement method không dùng |
| **D** – Dependency Inversion | Application phụ thuộc `IOrderRepository`, `IEventPublisher` (abstraction); Infrastructure implement; Api đăng ký qua DI. Đổi RabbitMQ sang Kafka chỉ sửa Infrastructure. | `new SqlConnection()` hay `new RabbitMqClient()` trong handler/domain |

### Ví dụ code minh họa

**D + S: handler chỉ phụ thuộc abstraction, làm đúng một việc**

```csharp
// Order.Application/Abstractions
public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken ct);
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct);
}

public interface IEventPublisher
{
    Task PublishAsync<T>(T @event, CancellationToken ct) where T : class;
}

// Order.Application/Orders/Commands/CreateOrder
public sealed record CreateOrderCommand(Guid CustomerId, List<OrderItemDto> Items);

public sealed class CreateOrderHandler(
    IOrderRepository orders,
    IUnitOfWork uow,
    IEventPublisher publisher)
{
    public async Task<Guid> HandleAsync(CreateOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.Items);   // luật nghiệp vụ nằm ở Domain
        await orders.AddAsync(order, ct);
        await uow.SaveChangesAsync(ct);
        await publisher.PublishAsync(new OrderCreated(order.Id), ct);
        return order.Id;
    }
}
```

**O + L: mở rộng bằng cách thêm class, không sửa code cũ**

```csharp
public interface IPaymentProvider
{
    string Name { get; }
    Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
}

public sealed class MomoProvider : IPaymentProvider { /* ... */ }
public sealed class VnPayProvider : IPaymentProvider { /* ... */ }

public sealed class PaymentService(IEnumerable<IPaymentProvider> providers)
{
    public Task<PaymentResult> PayAsync(string method, PaymentRequest req, CancellationToken ct)
        => providers.First(p => p.Name == method).ChargeAsync(req, ct);
}
// Thêm ZaloPay = thêm 1 class + đăng ký DI. PaymentService không đổi.
```

**I: tách interface đọc/ghi**

```csharp
public interface IOrderReader { Task<OrderDto?> GetAsync(Guid id, CancellationToken ct); }
public interface IOrderWriter { Task AddAsync(Order order, CancellationToken ct); }
// Query handler chỉ nhận IOrderReader (có thể đọc từ read replica/Redis).
```

**Composition root (chỉ ở Api)**

```csharp
builder.Services
    .AddApplication()                       // handler, behavior
    .AddInfrastructure(builder.Configuration); // EF Core, MassTransit, repository
```

### Áp dụng SOLID ở mức hệ thống (microservice)

| Nguyên lý | Ở mức service |
|---|---|
| S | Một service = một bounded context, một lý do để thay đổi |
| O | Thêm tính năng bằng service/consumer mới subscribe event, không sửa service cũ |
| L | Service mới thay thế version cũ mà vẫn tuân thủ contract (API/event) |
| I | Contract nhỏ, theo nhu cầu consumer; mỗi gRPC service chỉ expose method cần thiết |
| D | Service giao tiếp qua contract (gRPC proto, event schema) trong `BuildingBlocks.Contracts`, không biết implementation của nhau |

### Checklist SOLID cho mỗi service
- [ ] 4 project Domain / Application / Infrastructure / Api đúng quy tắc tham chiếu
- [ ] Domain không dùng EF Core, MassTransit hay thư viện hạ tầng nào
- [ ] Mỗi use case một handler, không có "God service"
- [ ] Mọi phụ thuộc hạ tầng đi qua interface, đăng ký bằng DI
- [ ] Có unit test cho Domain/Application bằng fake, không cần DB hay broker
- [ ] Có architecture test chặn vi phạm hướng phụ thuộc

---

## 3.2. Frontend (Next.js, Hướng 1)

**Quyết định:** dùng Next.js + Tailwind + shadcn/ui. FE là một service độc lập, stateless, giao tiếp với backend **chỉ qua API Gateway (YARP)**, không gọi trực tiếp từng microservice.

### Vì sao chọn hướng này
- FE stateless, đặt sau CDN, scale ngang độc lập với backend (phục vụ phần scale up/down).
- SSR/ISR + tối ưu ảnh cho storefront nhanh, đo được bằng k6/Lighthouse.
- Có nhiều mẫu giao diện miễn phí chất lượng cao, tiết kiệm thời gian UI.
- Minh họa "mỗi service một công nghệ phù hợp"; đồng thời là ví dụ cho trở ngại "phức tạp" (thêm toolchain).

### Hai ứng dụng
| App | Mục đích | Mẫu tham khảo | Trang chính |
|---|---|---|---|
| `storefront` | Khách mua hàng, cần SEO và tốc độ | QuickCart, Next E-commerce Shopco | Trang chủ, danh sách và chi tiết sản phẩm, giỏ hàng, checkout, đơn hàng của tôi |
| `admin` | Quản trị nội bộ | next-shadcn-dashboard-starter (MIT) | Dashboard, quản lý sản phẩm, đơn hàng, tồn kho |

> Kiểm tra giấy phép từng repo mẫu trước khi dùng. Mẫu gắn Shopify/Saleor chỉ lấy phần giao diện, thay lớp gọi API sang Gateway.

### Cấu trúc thư mục (feature-based, giữ tinh thần SOLID)

```
Web/storefront/
├── src/
│   ├── app/                       # Route (App Router), layout, loading, error
│   │   ├── (shop)/products/
│   │   ├── (shop)/cart/
│   │   ├── (shop)/checkout/
│   │   └── api/                   # Route handler / BFF mỏng (nếu cần)
│   ├── features/                  # Mỗi tính năng một thư mục tự chứa
│   │   ├── catalog/               # components, hooks, services, types
│   │   ├── basket/
│   │   └── orders/
│   ├── components/ui/             # shadcn/ui, component dùng chung
│   ├── lib/
│   │   ├── api/                   # Client sinh từ OpenAPI + wrapper (timeout, retry)
│   │   ├── auth/                  # Auth.js + Keycloak
│   │   └── config/
│   └── types/
├── public/
├── Dockerfile                     # output: standalone, image nhỏ
└── next.config.ts
```

**Quy tắc (SOLID cho FE):**
- **S**: component chỉ hiển thị; logic gọi API nằm ở `features/*/services`, state nằm ở hook.
- **D**: component phụ thuộc hàm/service trừu tượng (`catalogService.getProducts`), không gọi `fetch` trực tiếp.
- **O/I**: thêm tính năng bằng thư mục `features/` mới, mỗi service/hook nhỏ và tách theo nhu cầu.
- Không import chéo giữa các `features/` (có thể chặn bằng ESLint rule `no-restricted-imports`).

### Cách FE nói chuyện với backend
```
Browser → Next.js (SSR / route handler) → API Gateway (YARP) → Services
```
- Một nơi duy nhất cấu hình `API_BASE_URL` trỏ vào Gateway.
- Sinh client TypeScript từ OpenAPI của Gateway (`openapi-typescript`), tránh tự viết type tay và giữ đồng bộ contract.
- Token (JWT) giữ ở phía server (cookie httpOnly qua Auth.js), không để trong localStorage.
- Server Component gọi Gateway để render sớm; phần tương tác dùng TanStack Query.
- Có timeout, retry giới hạn và hiển thị lỗi thân thiện khi service phía sau lỗi (liên quan circuit breaker ở Gateway).

### Triển khai và hiệu năng FE
- Docker: `output: "standalone"`, image nhỏ; chạy bằng Aspire (`AddNpmApp`/container) ở dev.
- K8s: Deployment + HPA theo CPU, readiness/liveness probe, đặt sau CDN/Ingress.
- Tối ưu: ISR cho trang sản phẩm, `next/image`, cache `fetch` có `revalidate`, code splitting, bundle analyzer.
- Đo: Lighthouse (LCP, TTFB) cùng k6 trước/sau tối ưu.

### Checklist FE
- [ ] Hai app `storefront` và `admin` khởi tạo, chạy được bằng một lệnh cùng backend
- [ ] Client TypeScript sinh tự động từ OpenAPI, có script trong CI
- [ ] Đăng nhập Keycloak, bảo vệ route admin theo role
- [ ] Luồng mua hàng hoàn chỉnh: duyệt sản phẩm → giỏ → checkout → theo dõi đơn
- [ ] Dockerfile standalone, manifest K8s + HPA
- [ ] Báo cáo Lighthouse trước/sau tối ưu

---

## 4. Lộ trình thực hiện (6 tuần)

### Tuần 1: Kiến trúc
- [ ] Phân tích bài toán, xác định bounded context (DDD)
- [ ] Vẽ sơ đồ kiến trúc, chốt pattern
- [ ] Viết `docs/architecture.md` (monolith vs microservice, khi nào nên tách)
- [ ] Khởi tạo solution, repo, quy ước code, CI cơ bản
- [ ] Dựng template service theo SOLID/Clean Architecture (mục 3.1) để mọi service dùng chung
- [ ] Thêm architecture test (NetArchTest) vào CI

### Tuần 2: Service nền tảng
- [x] Catalog (CRUD, EF Core, PostgreSQL, gRPC server)
- [x] Basket (Redis, gọi gRPC sang Catalog)
- [x] API Gateway (YARP, routing, rate limit)
- [ ] Identity (giai đoạn đầu JWT nội bộ, sau đó Keycloak — xem EXECUTION_OPTIONS)
- [ ] Dockerize từng service, chạy bằng Aspire (hiện: hạ tầng chạy bằng `docker-compose.infra.yml`)
- [ ] Khởi tạo `Web/storefront` và `Web/admin` (Next.js + shadcn/ui), lấy mẫu giao diện, cấu hình gọi qua Gateway
- [ ] Sinh client TypeScript từ OpenAPI, đăng nhập Keycloak (Auth.js)
- [ ] Storefront: danh sách và chi tiết sản phẩm, giỏ hàng (nối Catalog, Basket)

### Tuần 3: Event-driven và Saga
- [ ] RabbitMQ + MassTransit trong BuildingBlocks
- [ ] Order, Inventory, Payment, Notification
- [ ] Cài Saga + compensating action
- [ ] Outbox pattern, idempotent consumer
- [ ] Integration test cho luồng đặt hàng
- [ ] Storefront: checkout, theo dõi trạng thái đơn (hiển thị các trạng thái Saga)
- [ ] Admin: dashboard, quản lý sản phẩm, đơn hàng, tồn kho

### Tuần 4: Scale up, scale down, best performance
- [ ] Observability: OpenTelemetry → Prometheus, Grafana, Jaeger
- [ ] Viết k6 script, đo **baseline** (RPS, p95/p99 latency, CPU/RAM)
- [ ] Tối ưu: Redis cache, response compression, gRPC nội bộ, async qua queue, read replica, connection pooling
- [ ] Kubernetes: HPA (CPU/RAM), KEDA (độ dài queue, scale về 0)
- [ ] Health check, readiness/liveness probe, resource requests/limits
- [ ] FE: Dockerfile standalone, HPA cho FE, ISR + tối ưu ảnh, đo Lighthouse trước/sau
- [ ] Đo lại, so sánh trước/sau, ghi vào `docs/benchmark-report.md`

### Tuần 5: Sai lầm làm hỏng kiến trúc
Mỗi mục gồm: demo lỗi → hậu quả → cách sửa (lưu trong `docs/anti-patterns.md`).

| # | Sai lầm | Hậu quả | Cách khắc phục |
|---|---|---|---|
| 1 | Distributed monolith (gọi đồng bộ chuỗi, deploy cùng lúc) | Mất lợi ích tách service | Async event, giảm coupling |
| 2 | Dùng chung database | Phụ thuộc ngầm, không deploy độc lập | Database per service |
| 3 | Tách quá nhỏ (nano-service) | Phức tạp, độ trễ tăng | Tách theo bounded context |
| 4 | Không timeout/retry/circuit breaker | Cascading failure | Polly resilience |
| 5 | Thiếu observability | Không debug được | Log + trace + metric tập trung |
| 6 | Không idempotency / bỏ qua eventual consistency | Trùng đơn, dữ liệu lệch | Idempotency key, Outbox, Saga |
| 7 | Không versioning API/contract | Thay đổi làm vỡ service khác | Versioning, contract test |
| 8 | Không CI/CD, deploy thủ công | Chậm, dễ lỗi | Pipeline tự động |
| 9 | Chọn microservice quá sớm (team nhỏ, domain chưa rõ) | Chi phí vượt lợi ích | Bắt đầu modular monolith |

- [ ] Viết code demo cho ít nhất 3 lỗi tiêu biểu (1, 2, 4) và bản sửa

### Tuần 6: Tổng kết
- [ ] Bảng đánh đổi: phức tạp, độ trễ, tốn tài nguyên và cách giảm thiểu
- [ ] Demo end-to-end
- [ ] Hoàn thiện tài liệu, slide thuyết trình

---

## 5. Xử lý các trở ngại

| Trở ngại | Giải pháp trong dự án |
|---|---|
| Phức tạp | .NET Aspire, template service chung, CI/CD tự động, ranh giới domain rõ |
| Độ trễ | gRPC nội bộ, Redis cache, async messaging, giảm số hop |
| Tốn tài nguyên | KEDA scale về 0, image nhỏ (chiseled/AOT), giới hạn resource hợp lý |

---

## 6. Thêm service mới (lệnh mẫu)

Solution (`Ecommerce.slnx`), Gateway, BuildingBlocks, Catalog, Basket đã có. Version package quản lý tập trung trong `Directory.Packages.props` (Central Package Management): `dotnet add package` ghi version vào file này, `.csproj` chỉ chứa tên package. Ví dụ thêm Order service theo cấu trúc mục 3.1:

```bash
# 4 project
dotnet new classlib -n Order.Domain         -o src/Services/Order/Order.Domain
dotnet new classlib -n Order.Application    -o src/Services/Order/Order.Application
dotnet new classlib -n Order.Infrastructure -o src/Services/Order/Order.Infrastructure
dotnet new web      -n Order.Api            -o src/Services/Order/Order.Api

# Tham chiếu đúng hướng phụ thuộc
dotnet add src/Services/Order/Order.Application    reference src/Services/Order/Order.Domain
dotnet add src/Services/Order/Order.Infrastructure reference src/Services/Order/Order.Application
dotnet add src/Services/Order/Order.Api reference src/Services/Order/Order.Application src/Services/Order/Order.Infrastructure src/BuildingBlocks/BuildingBlocks

# Package
dotnet add src/Services/Order/Order.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Services/Order/Order.Infrastructure package MassTransit.RabbitMQ

# Thêm vào solution
dotnet sln Ecommerce.slnx add --solution-folder Services src/Services/Order/Order.Domain src/Services/Order/Order.Application src/Services/Order/Order.Infrastructure src/Services/Order/Order.Api
```

Sau đó: thêm DB vào `POSTGRES_MULTIPLE_DATABASES` trong `deploy/docker/docker-compose.infra.yml`, thêm route/cluster vào `src/ApiGateway/appsettings.json`.

---

## 7. Tiêu chí hoàn thành (Definition of Done)

- [ ] Toàn bộ service chạy được bằng một lệnh (Aspire hoặc docker-compose)
- [ ] Luồng đặt hàng chạy đúng cả trường hợp thành công và thất bại (có bù trừ)
- [ ] Có dashboard Grafana và trace xuyên service
- [ ] Báo cáo benchmark trước/sau tối ưu, minh họa scale up/down trên Kubernetes
- [ ] Có demo và tài liệu cho ít nhất 3 sai lầm kiến trúc
- [ ] CI chạy build + test tự động
- [ ] Mọi service tuân thủ cấu trúc SOLID/Clean Architecture, architecture test đều pass
- [ ] Storefront và Admin chạy được, hoàn thành luồng mua hàng từ giao diện
- [ ] Slide thuyết trình hoàn chỉnh

---

## 8. Bàn giao

1. Sơ đồ kiến trúc (`docs/architecture.md`)
2. Source code demo e-commerce
3. File Docker và Kubernetes (`deploy/`), gồm cả frontend
4. Source frontend `Web/storefront` và `Web/admin`
5. Báo cáo load test (`docs/benchmark-report.md`)
6. Tài liệu anti-patterns (`docs/anti-patterns.md`)
7. Slide thuyết trình

---

## 9. Giả định cần xác nhận

- Dự án phục vụ học tập / thuyết trình hay sản phẩm thật?
- Quy mô team và thời gian thực tế (kế hoạch trên giả định 6 tuần).
- Môi trường chạy: máy local, cloud (Azure/AWS) hay on-premise?
