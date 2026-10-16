using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Database.Providers.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessibilityTrackFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PreferAudioDescription",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAudioDescription",
                table: "MediaStreamInfos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCommentary",
                table: "MediaStreamInfos",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferAudioDescription",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsAudioDescription",
                table: "MediaStreamInfos");

            migrationBuilder.DropColumn(
                name: "IsCommentary",
                table: "MediaStreamInfos");
        }
    }
}
