using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HQ.Plugins.Tasks.Migrations
{
    /// <inheritdoc />
    public partial class AgentScopedTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A task is now either project-scoped (ProjectId set) or agent-scoped (AgentId set),
            // so ProjectId becomes nullable and agent ownership columns are added.
            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                schema: "plugin_localtasks",
                table: "tasks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentId",
                schema: "plugin_localtasks",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentName",
                schema: "plugin_localtasks",
                table: "tasks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_OrganizationId_AgentId_Status",
                schema: "plugin_localtasks",
                table: "tasks",
                columns: new[] { "OrganizationId", "AgentId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tasks_OrganizationId_AgentId_Status",
                schema: "plugin_localtasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AgentId",
                schema: "plugin_localtasks",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AgentName",
                schema: "plugin_localtasks",
                table: "tasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "ProjectId",
                schema: "plugin_localtasks",
                table: "tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
