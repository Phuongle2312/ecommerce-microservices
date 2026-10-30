# E-Commerce Microservices (.NET 10)

Hệ thống mẫu nghiên cứu kiến trúc Microservice. Kế hoạch: [PROJECT_PLAN.md](PROJECT_PLAN.md), phương án triển khai: [EXECUTION_OPTIONS.md](EXECUTION_OPTIONS.md).

**Trạng thái:** Giai đoạn 1 xong — Catalog, Basket, API Gateway. Tiếp theo: Giai đoạn 2 (RabbitMQ, Order/Inventory/Payment/Notification, Saga).

Ở giai đoạn 1 mỗi service là một project `*.Api` (chia thư mục `Domain/`, `Infrastructure/`, `Endpoints/`); cấu trúc Clean Architecture 4 project là đích, xem [PROJECT_PLAN.md](PROJECT_PLAN.md) mục 3.1.

## Cấu trúc

```
src/
├── ApiGateway/                     YARP reverse proxy + rate limiting      :5000
├── BuildingBlocks/
│   ├── BuildingBlocks/             Logging (Serilog), ProblemDetails, health checks, pagination
│   └── BuildingBlocks.Contracts/   gRPC protos (sau này: integration events)
└── Services/
    ├── Catalog/Catalog.Api/        REST :5101, gRPC :5102, PostgreSQL (catalogdb)
    └── Basket/Basket.Api/          REST :5201, Redis, gRPC client -> Catalog
deploy/docker/docker-compose.infra.yml   PostgreSQL :5432, Redis :6380
```

## Chạy local

```bash
docker compose -f deploy/docker/docker-compose.infra.yml up -d --wait
dotnet build Ecommerce.slnx
dotnet run --project src/Services/Catalog/Catalog.Api   # tự migrate + seed ở môi trường Development
dotnet run --project src/Services/Basket/Basket.Api
dotnet run --project src/ApiGateway
```

Redis trong compose dùng cổng **6380** trên host để không đụng Redis cài sẵn ở 6379.

## API qua Gateway

| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/catalog/products?pageIndex=1&pageSize=10&categoryId=&search=` | Danh sách sản phẩm (phân trang) |
| GET/PUT/DELETE | `/api/catalog/products/{id}` | Chi tiết / sửa / xóa |
| POST | `/api/catalog/products` | Tạo sản phẩm |
| GET/POST | `/api/catalog/categories` | Danh mục |
| GET/DELETE | `/api/basket/baskets/{userName}` | Xem / xóa giỏ |
| PUT | `/api/basket/baskets/{userName}/items` | Thêm/sửa item `{ "productId", "quantity" }` — giá và tồn kho lấy từ Catalog qua gRPC |
| DELETE | `/api/basket/baskets/{userName}/items/{productId}` | Bỏ item |

Mỗi service có `/health/live` (process) và `/health/ready` (kèm DB/Redis). Lỗi trả về theo chuẩn ProblemDetails (RFC 7807).

## Migration (Catalog)

```bash
dotnet ef migrations add <Name> --project src/Services/Catalog/Catalog.Api -o Infrastructure/Migrations
```
