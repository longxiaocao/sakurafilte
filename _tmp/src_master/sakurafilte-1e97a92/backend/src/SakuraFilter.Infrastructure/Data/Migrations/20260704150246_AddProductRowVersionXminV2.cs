using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SakuraFilter.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// E2E BD.3 修复 v2: 为 Product 实体添加乐观锁并发令牌 (使用 PG 系统列 xmin)
    ///   WHY: 之前无并发令牌, 两个管理员同时编辑同一产品会导致 lost update
    ///   方案: Product.RowVersion (uint) 映射到 PG 系统列 xmin (type=xid)
    ///         EF Core IsRowVersion() 在 UPDATE 时自动 SET WHERE xmin = @original
    ///   注意: 此 migration 是空操作 (xmin 系统列自动存在, 不能 ALTER TABLE 添加/删除)
    ///         保留空 Up/Down 仅用于更新 ModelSnapshot, 让 EF Core 知道此列已被模型使用
    ///   历史: v1 用 byte[] + [Timestamp] 抛 InvalidCastException (xid 不能读为 byte[]), v2 改用 uint
    /// </summary>
    public partial class AddProductRowVersionXminV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 空操作: xmin 是 PostgreSQL 系统列, 每个表自动存在, 无需 ALTER TABLE
            // EF Core 通过 Product.RowVersion (uint) shadow property 读取此列
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 空操作: 不能删除系统列
        }
    }
}
