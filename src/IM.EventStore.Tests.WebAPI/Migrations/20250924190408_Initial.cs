using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace IM.EventStore.Tests.WebAPI.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderView",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderView", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "streams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_streams", x => x.id);
                    table.UniqueConstraint("AK_streams_id_tenant_id", x => new { x.id, x.tenant_id });
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_type = table.Column<string>(type: "text", nullable: false),
                    current_sequence = table.Column<long>(type: "bigint", nullable: false),
                    lease_owner = table.Column<string>(type: "text", nullable: true),
                    lease_expires_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    stream_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => new { x.stream_id, x.version });
                    table.ForeignKey(
                        name: "FK_events_streams_stream_id_tenant_id",
                        columns: x => new { x.stream_id, x.tenant_id },
                        principalTable: "streams",
                        principalColumns: new[] { "id", "tenant_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_events_stream_id_tenant_id",
                table: "events",
                columns: new[] { "stream_id", "tenant_id" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id",
                table: "events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_stream_id",
                table: "events",
                columns: new[] { "tenant_id", "stream_id" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_timestamp",
                table: "events",
                columns: new[] { "tenant_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_type",
                table: "events",
                columns: new[] { "tenant_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_streams_tenant_id",
                table: "streams",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_streams_tenant_id_created_timestamp",
                table: "streams",
                columns: new[] { "tenant_id", "created_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_streams_tenant_id_updated_timestamp",
                table: "streams",
                columns: new[] { "tenant_id", "updated_timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_streams_tenant_id_version",
                table: "streams",
                columns: new[] { "tenant_id", "version" });

            migrationBuilder.CreateIndex(
                name: "ux_streams_tenant_id_id",
                table: "streams",
                columns: new[] { "tenant_id", "id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_subscriptions_subscription_type",
                table: "subscriptions",
                column: "subscription_type",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "OrderView");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "streams");
        }
    }
}
