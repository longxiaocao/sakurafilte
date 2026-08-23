-- Day 9.11 修复: EF Core 启用 UseSnakeCaseNamingConvention 后,__EFMigrationsHistory 列名应为 snake_case
--   错误: 之前用 "MigrationId"/"ProductVersion" (PascalCase)
--   正确: migration_id / product_version (snake_case, 与 EF Core 查询一致)
DROP TABLE IF EXISTS "__EFMigrationsHistory";

CREATE TABLE "__EFMigrationsHistory" (
    migration_id varchar(150) NOT NULL,
    product_version varchar(32) NOT NULL,
    CONSTRAINT "pk___EFMigrationsHistory" PRIMARY KEY (migration_id)
);

INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
VALUES ('20260702025150_InitialCreate', '8.0.10');

SELECT * FROM "__EFMigrationsHistory";
