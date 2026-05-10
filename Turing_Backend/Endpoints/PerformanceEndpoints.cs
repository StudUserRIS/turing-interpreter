// Turing_Backend/Endpoints/PerformanceEndpoints.cs
using Dapper;
using Turing_Backend.Database;
using Turing_Backend.Models;

namespace Turing_Backend.Endpoints;

public static class PerformanceEndpoints
{
    public static void MapPerformanceEndpoints(this WebApplication app)
    {
        static bool IsTeacherOrAdmin(HttpContext ctx)
        {
            var role = ctx.Items["Role"] as string;
            return role == "Teacher" || role == "Admin";
        }

        app.MapGet("/api/Performance/global", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            string role = (string)ctx.Items["Role"]!;
            int userId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            // Преподаватель видит данные только по своим курсам, админ — все.
            // В успеваемости учитываются только опубликованные задания. Архивные не показываются.
            string filter = role == "Teacher" ? " AND c.TeacherId = @userId " : "";

            var perf = await db.QueryAsync<PerformanceRecord>($@"
                SELECT c.Name as Course, c.GradingPolicy, g.Name as ""Group"", u.FullName as Name,
                       a.Title as Task, a.Type, s.Grade,
                       COALESCE(s.Status, 'Не сдано') as Status
                FROM Users u
                JOIN Groups g ON u.GroupId = g.Id
                JOIN CourseGroups cg ON cg.GroupId = u.GroupId
                JOIN Courses c ON c.Id = cg.CourseId
                LEFT JOIN Assignments a ON c.Id = a.CourseId AND a.Status = 'Опубликовано'
                LEFT JOIN Submissions s ON a.Id = s.AssignmentId AND u.Id = s.StudentId
                WHERE u.Role = 'Student' {filter}", new { userId });
            return Results.Ok(perf);
        });

        app.MapGet("/api/Performance/my", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            int studentId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();
            var perf = await db.QueryAsync<StudentTaskRecord>(@"
                SELECT c.Name as Course, c.GradingPolicy, a.Title as Task, a.Type,
                       a.Deadline, s.Grade,
                       COALESCE(s.Status, 'Не сдано') as Status
                FROM Users u
                JOIN CourseGroups cg ON cg.GroupId = u.GroupId
                JOIN Courses c ON c.Id = cg.CourseId AND c.Archived = 0
                JOIN Assignments a ON c.Id = a.CourseId AND a.Status = 'Опубликовано'
                LEFT JOIN Submissions s ON a.Id = s.AssignmentId AND s.StudentId = @studentId
                WHERE u.Id = @studentId", new { studentId });
            return Results.Ok(perf);
        });
    }
}
