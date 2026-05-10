using Dapper;
using Turing_Backend.Common;
using Turing_Backend.Database;
using Turing_Backend.Dtos;
using Turing_Backend.Hubs;
using Turing_Backend.Models;
using Turing_Backend.Services;

namespace Turing_Backend.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        static bool IsTeacherOrAdmin(HttpContext ctx)
        {
            var role = ctx.Items["Role"] as string;
            return role == "Teacher" || role == "Admin";
        }

        app.MapGet("/api/groups", async (DbConnectionFactory dbFactory) =>
        {
            using var db = dbFactory.Create();
            var groups = await db.QueryAsync<Group>("SELECT * FROM Groups ORDER BY Name");
            return Results.Ok(groups);
        });

        app.MapPost("/api/groups", async (CreateGroupDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();

            var (ok, err) = ValidationRules.ValidateGroupName(dto.Name);
            if (!ok) return Results.BadRequest(new { Error = err });

            using var db = dbFactory.Create();
            string normalized = dto.Name.Trim();

            // Проверка дубликата: имя группы должно быть уникальным во всей системе.
            // На дубликат отвечаем 409 с Reason="DuplicateName" — это позволяет клиенту
            // отличить занятость имени от других конфликтов (устаревшая версия и т.п.)
            // и показать пользователю конкретное сообщение, не перезагружая окно.
            var dup = await db.QueryFirstOrDefaultAsync<Group>(
                "SELECT * FROM Groups WHERE LOWER(Name) = LOWER(@Name)", new { Name = normalized });
            if (dup != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Группа с названием «{normalized}» уже существует. Выберите другое название.");

            int newId;
            try
            {
                newId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Groups (Name) VALUES (@Name) RETURNING Id", new { Name = normalized });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                // Гонка двух одновременных вставок: SELECT-проверка прошла у обоих,
                // но UNIQUE-индекс ux_groups_name_ci пропустит только первую.
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Группа с названием «{normalized}» уже существует. Выберите другое название.");
            }

            // Push-обновление: вкладки "Группы", фильтры по группе на других вкладках —
            // у всех преподавателей и админов должны мгновенно подхватить новую группу.
            await notify.BroadcastDataChangedAsync("Group", "Created", newId);
            return Results.Ok();
        });

        app.MapPut("/api/groups/{id:int}", async (int id, UpdateGroupDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var existing = await db.QueryFirstOrDefaultAsync<Group>(
                "SELECT * FROM Groups WHERE Id = @Id", new { Id = id });
            if (existing == null)
                return Common.ApiResults.Gone("GroupDeleted",
                    "Группа, которую вы редактировали, была удалена другим администратором. Список групп будет обновлён.");

            if (existing.Version != dto.Version)
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Название группы «{existing.Name}» было изменено другим администратором. Ваши изменения не сохранены — окно будет закрыто, откройте его заново для актуальных данных.",
                    existing);

            var (ok, err) = ValidationRules.ValidateGroupName(dto.Name);
            if (!ok) return Results.BadRequest(new { Error = err });

            string normalized = dto.Name.Trim();
            // Проверка дубликата: разрешаем оставить прежнее имя, но не разрешаем
            // занять имя другой существующей группы.
            var dup = await db.QueryFirstOrDefaultAsync<Group>(
                "SELECT * FROM Groups WHERE LOWER(Name) = LOWER(@Name) AND Id <> @Id",
                new { Name = normalized, Id = id });
            if (dup != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Группа с названием «{normalized}» уже существует. Выберите другое название.");

            int affected;
            try
            {
                affected = await db.ExecuteAsync(
                    "UPDATE Groups SET Name = @Name, Version = Version + 1, UpdatedAt = NOW() WHERE Id = @Id AND Version = @Version",
                    new { Name = normalized, Id = id, dto.Version });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Группа с названием «{normalized}» уже существует. Выберите другое название.");
            }
            if (affected == 0)
            {
                var fresh = await db.QueryFirstOrDefaultAsync<Group>("SELECT * FROM Groups WHERE Id = @Id", new { Id = id });
                if (fresh == null)
                    return Common.ApiResults.Gone("GroupDeleted",
                        "Группа была удалена другим администратором в момент сохранения.");
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Группа «{fresh.Name}» была изменена другим администратором в момент сохранения. Окно будет закрыто, откройте его заново для актуальных данных.", fresh);
            }

            // Push-обновление: переименование группы должно отразиться во всех окнах
            // (вкладка "Группы", столбец "Группа" на вкладке "Студенты" и т.п.).
            await notify.BroadcastDataChangedAsync("Group", "Updated", id);
            return Results.Ok();
        });

        app.MapDelete("/api/groups/{id:int}", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var rows = await db.ExecuteAsync("DELETE FROM Groups WHERE Id = @Id", new { Id = id });
            if (rows == 0)
                return Common.ApiResults.Gone("GroupDeleted",
                    "Эта группа уже была удалена другим администратором.");

            // Push-обновление: каскадно меняется состав студентов (GroupId → NULL),
            // привязки CourseGroups и т.п. — на клиентах нужно обновить таблицы групп,
            // студентов и фильтры.
            await notify.BroadcastDataChangedAsync("Group", "Deleted", id);
            return Results.Ok();
        });

        app.MapGet("/api/courses/my", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            int userId = (int)ctx.Items["UserId"]!;
            string role = (string)ctx.Items["Role"]!;
            using var db = dbFactory.Create();

            string sql;
            object args;

            if (role == "Admin")
            {
                sql = @"SELECT c.Id, c.Name, c.TeacherId, c.GradingPolicy, c.Description, c.Archived, c.Version,
                               u.FullName as TeacherName
                        FROM Courses c
                        LEFT JOIN Users u ON c.TeacherId = u.Id
                        ORDER BY c.Archived ASC, c.Name";
                args = new { };
            }
            else if (role == "Teacher")
            {
                sql = @"SELECT c.Id, c.Name, c.TeacherId, c.GradingPolicy, c.Description, c.Archived, c.Version,
                               u.FullName as TeacherName
                        FROM Courses c
                        LEFT JOIN Users u ON c.TeacherId = u.Id
                        WHERE c.TeacherId = @userId
                        ORDER BY c.Archived ASC, c.Name";
                args = new { userId };
            }
            else
            {
                sql = @"SELECT DISTINCT c.Id, c.Name, c.TeacherId, c.GradingPolicy, c.Description, c.Archived, c.Version,
                                       u.FullName as TeacherName
                        FROM Courses c
                        LEFT JOIN Users u ON c.TeacherId = u.Id
                        JOIN CourseGroups cg ON cg.CourseId = c.Id
                        JOIN Users me ON me.Id = @userId
                        WHERE cg.GroupId = me.GroupId AND c.Archived = 0
                        ORDER BY c.Name";
                args = new { userId };
            }

            var courses = await db.QueryAsync<Course>(sql, args);
            return Results.Ok(courses);
        });

        app.MapPost("/api/courses", async (CreateCourseDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            string role = (string)ctx.Items["Role"]!;
            int currentUserId = (int)ctx.Items["UserId"]!;

            var (ok, err) = ValidationRules.ValidateCourseName(dto.Name);
            if (!ok) return Results.BadRequest(new { Error = err });

            int teacherId;
            if (role == "Admin")
            {
                if (!dto.TeacherId.HasValue)
                    return Results.BadRequest(new { Error = "Администратор обязан выбрать преподавателя курса." });
                teacherId = dto.TeacherId.Value;
            }
            else
            {
                teacherId = currentUserId;
            }

            using var db = dbFactory.Create();

            var teacher = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id AND (Role = 'Teacher' OR Role = 'Admin')",
                new { Id = teacherId });
            if (teacher == null)
                return Results.BadRequest(new { Error = "Указанный пользователь не является преподавателем." });

            string normalized = dto.Name.Trim();
            // Проверка дубликата: имя курса должно быть уникальным в системе (без учёта регистра).
            // Это предотвращает путаницу, когда у преподавателей и студентов в списках курсов
            // оказываются два «одинаковых» курса.
            var dupCourse = await db.QueryFirstOrDefaultAsync<Course>(
                "SELECT * FROM Courses WHERE LOWER(Name) = LOWER(@Name)", new { Name = normalized });
            if (dupCourse != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Курс с названием «{normalized}» уже существует в системе. Выберите другое название.");

            int newId;
            try
            {
                newId = await db.QuerySingleAsync<int>(
                    "INSERT INTO Courses (Name, TeacherId, Description) VALUES (@Name, @TeacherId, @Description) RETURNING Id",
                    new { Name = normalized, TeacherId = teacherId, dto.Description });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Курс с названием «{normalized}» уже существует в системе. Выберите другое название.");
            }

            // Push-обновление: новый курс появился — у преподавателей/админов в "Мои курсы"
            // и в "Администрирование" таблица курсов должна обновиться автоматически.
            await notify.BroadcastDataChangedAsync("Course", "Created", newId);
            return Results.Ok(new { Id = newId });
        });

        app.MapPut("/api/courses/{id:int}", async (int id, UpdateCourseDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            return await UpdateCourseInternal(id, dto, ctx, dbFactory, notify);
        });

        app.MapPut("/api/courses/{id:int}/meta", async (int id, UpdateCourseDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            return await UpdateCourseInternal(id, dto, ctx, dbFactory, notify);
        });

        app.MapDelete("/api/courses/{id:int}", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            string role = (string)ctx.Items["Role"]!;
            int currentUserId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var assignmentIds = (await db.QueryAsync<int>(
                "SELECT Id FROM Assignments WHERE CourseId = @CourseId",
                new { CourseId = id })).ToList();

            int rows;
            if (role == "Teacher")
            {
                rows = await db.ExecuteAsync(
                    "DELETE FROM Courses WHERE Id = @Id AND TeacherId = @TeacherId",
                    new { Id = id, TeacherId = currentUserId });
            }
            else
            {
                rows = await db.ExecuteAsync("DELETE FROM Courses WHERE Id = @Id", new { Id = id });
            }
            if (rows == 0)
                return Common.ApiResults.Gone("CourseDeleted",
                    "Этот курс уже был удалён другим пользователем.");

            foreach (var aid in assignmentIds)
            {
                await notify.NotifyAssignmentChangedAsync(aid, "CourseDeleted",
                    "Курс этого задания был удалён преподавателем. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения.");
            }
            // Push-обновление: список курсов и связанные с ним задания должны исчезнуть
            // у всех пользователей. Также обновляются вкладки "Студенты и оценки" / "Мои курсы".
            await notify.BroadcastDataChangedAsync("Course", "Deleted", id);
            return Results.Ok();
        });

        app.MapGet("/api/courses/{id:int}/groups", async (int id, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var groupIds = await db.QueryAsync<int>(
                "SELECT GroupId FROM CourseGroups WHERE CourseId = @id",
                new { id });
            return Results.Ok(groupIds);
        });

        app.MapPut("/api/courses/{id:int}/groups", async (int id, List<int> groupIds, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var courseExists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Courses WHERE Id = @id", new { id });
            if (courseExists == 0)
                return Common.ApiResults.Gone("CourseDeleted",
                    "Курс был удалён другим пользователем — изменить привязки групп невозможно. Список курсов будет обновлён.");

            await db.ExecuteAsync("DELETE FROM CourseGroups WHERE CourseId = @id", new { id });
            foreach (var gid in groupIds)
            {
                await db.ExecuteAsync(
                    "INSERT INTO CourseGroups (CourseId, GroupId) VALUES (@id, @gid)",
                    new { id, gid });
            }

            // Push-обновление: студенты привязанных/отвязанных групп должны увидеть
            // изменение состава доступных курсов в "Мои курсы".
            await notify.BroadcastDataChangedAsync("CourseGroups", "Updated", id);
            return Results.Ok();
        });

        // Полный метод PUT /api/courses/{id}/grading — изменение формулы оценки курса.
        // Изменение: инкрементируем Version курса, чтобы открытые окна редактирования
        // курса не получили устаревшую версию при следующем сохранении. Дополнительно
        // шлём broadcast как для GradingPolicy, так и для Course — итоговая оценка
        // в окнах "Мои курсы" и "Детализация оценок" пересчитается у всех клиентов.
        app.MapPut("/api/courses/{id:int}/grading", async (int id, UpdateGradingDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var rows = await db.ExecuteAsync(
                "UPDATE Courses SET GradingPolicy = @Policy, Version = Version + 1, UpdatedAt = NOW() WHERE Id = @Id",
                new { dto.Policy, Id = id });
            if (rows == 0)
                return Common.ApiResults.Gone("CourseDeleted",
                    "Курс был удалён другим пользователем — формула оценки не сохранена. Список курсов будет обновлён.");

            // Push-обновление: формула оценки изменилась — окна "Мои курсы" преподавателя/студента,
            // "Детализация оценок", "Формула оценки" должны пересчитать итоговые баллы автоматически.
            await notify.BroadcastDataChangedAsync("GradingPolicy", "Updated", id);
            await notify.BroadcastDataChangedAsync("Course", "Updated", id);
            return Results.Ok();
        });


        app.MapGet("/api/courses/{id:int}/students", async (int id, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var students = await db.QueryAsync(@"
                SELECT DISTINCT u.Id, u.FullName, u.Login, g.Id as GroupId, g.Name as GroupName
                FROM Users u
                JOIN Groups g ON u.GroupId = g.Id
                JOIN CourseGroups cg ON cg.GroupId = g.Id
                WHERE cg.CourseId = @id AND u.Role = 'Student'
                ORDER BY g.Name, u.FullName", new { id });
            return Results.Ok(students);
        });

        app.MapGet("/api/users/students", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var students = await db.QueryAsync<User>(
                "SELECT Id, Login, FullName, GroupId, Version FROM Users WHERE Role = 'Student'");
            return Results.Ok(students);
        });

        app.MapPost("/api/users", async (CreateUserDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();

            var (nameOk, nameErr) = ValidationRules.ValidateFullName(dto.FullName);
            if (!nameOk) return Results.BadRequest(new { Error = nameErr });

            var (lgOk, lgErr) = ValidationRules.ValidateLogin(dto.Login);
            if (!lgOk) return Results.BadRequest(new { Error = lgErr });

            var (pwOk, pwErr) = ValidationRules.ValidatePassword(dto.Password);
            if (!pwOk) return Results.BadRequest(new { Error = pwErr });

            using var db = dbFactory.Create();

            string trimmedLogin = dto.Login.Trim();
            // Проверка дубликата логина: в системе должен существовать только один пользователь
            // с таким логином (без учёта регистра — иначе можно зарегистрировать "Ivan" и "ivan").
            // Возвращаем 409 с Reason="DuplicateName", чтобы клиент мог отличить эту ошибку
            // от устаревших данных и показать пользователю конкретное сообщение.
            var existing = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE LOWER(Login) = LOWER(@Login)", new { Login = trimmedLogin });
            if (existing != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");

            var hashed = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            int newId;
            try
            {
                newId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Users (Login, Password, FullName, Role, GroupId) " +
                    "VALUES (@Login, @HashedPassword, @FullName, @Role, @GroupId) RETURNING Id",
                    new
                    {
                        Login = trimmedLogin,
                        HashedPassword = hashed,
                        FullName = ValidationRules.NormalizeFullName(dto.FullName),
                        dto.Role,
                        dto.GroupId
                    });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");
            }

            // Push-обновление: вкладки "Студенты"/"Преподаватели", фильтры по преподавателю
            // на вкладке "Курсы" — должны автоматически пополниться у всех админов.
            string entity = dto.Role == "Teacher" ? "Teacher" : "Student";
            await notify.BroadcastDataChangedAsync(entity, "Created", newId);
            return Results.Ok();
        });

        app.MapPut("/api/users/{id:int}", async (int id, UpdateUserDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();

            var (nameOk, nameErr) = ValidationRules.ValidateFullName(dto.FullName);
            if (!nameOk) return Results.BadRequest(new { Error = nameErr });

            var (lgOk, lgErr) = ValidationRules.ValidateLogin(dto.Login);
            if (!lgOk) return Results.BadRequest(new { Error = lgErr });

            if (!string.IsNullOrEmpty(dto.Password))
            {
                var (pwOk, pwErr) = ValidationRules.ValidatePassword(dto.Password);
                if (!pwOk) return Results.BadRequest(new { Error = pwErr });
            }

            using var db = dbFactory.Create();

            var existingUser = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = id });
            if (existingUser == null)
                return Common.ApiResults.Gone("UserDeleted",
                    "Этот пользователь был удалён другим администратором. Список будет обновлён.");

            if (dto.Version != 0 && existingUser.Version != dto.Version)
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Данные студента «{existingUser.FullName}» были изменены другим администратором. Чтобы не перезаписать чужие правки, окно будет закрыто. Откройте его заново для актуальных данных.",
                    existingUser);

            string trimmedLogin = dto.Login.Trim();
            string normalizedName = ValidationRules.NormalizeFullName(dto.FullName);

            // Проверка дубликата логина при редактировании: разрешаем оставить прежний логин,
            // но не разрешаем занять логин другого существующего пользователя.
            var conflict = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE LOWER(Login) = LOWER(@Login) AND Id <> @Id",
                new { Login = trimmedLogin, Id = id });
            if (conflict != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");

            bool loginChanged = !string.Equals(existingUser.Login, trimmedLogin, StringComparison.Ordinal);
            bool passwordChanged = !string.IsNullOrEmpty(dto.Password);
            bool nameChanged = !string.Equals(existingUser.FullName, normalizedName, StringComparison.Ordinal);
            bool groupChanged = existingUser.GroupId != dto.GroupId;

            int affected;
            try
            {
                if (string.IsNullOrEmpty(dto.Password))
                {
                    affected = await db.ExecuteAsync(
                        "UPDATE Users SET Login = @Login, FullName = @FullName, GroupId = @GroupId, Version = Version + 1, UpdatedAt = NOW() " +
                        "WHERE Id = @Id AND Version = @Version",
                        new { Login = trimmedLogin, FullName = normalizedName, dto.GroupId, Id = id, Version = existingUser.Version });
                }
                else
                {
                    var hashed = BCrypt.Net.BCrypt.HashPassword(dto.Password);
                    affected = await db.ExecuteAsync(
                        "UPDATE Users SET Login = @Login, Password = @HashedPassword, FullName = @FullName, GroupId = @GroupId, Version = Version + 1, UpdatedAt = NOW() " +
                        "WHERE Id = @Id AND Version = @Version",
                        new { Login = trimmedLogin, HashedPassword = hashed, FullName = normalizedName, dto.GroupId, Id = id, Version = existingUser.Version });
                }
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");
            }
            if (affected == 0)
            {
                var fresh = await db.QueryFirstOrDefaultAsync<User>("SELECT * FROM Users WHERE Id = @Id", new { Id = id });
                if (fresh == null)
                    return Common.ApiResults.Gone("UserDeleted",
                        "Пользователь был удалён другим администратором в момент сохранения.");
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Данные студента «{fresh.FullName}» были изменены другим администратором в момент сохранения. Окно будет закрыто, откройте его заново для актуальных данных.", fresh);
            }

            if (loginChanged || passwordChanged || nameChanged || groupChanged)
            {
                await notify.EndAllSessionsForUserAsync(id, "ProfileDataChanged",
                    "Данные вашего профиля были изменены. Сессия принудительно завершена.");
            }

            // Push-обновление: данные пользователя поменялись — таблицы "Студенты"/"Преподаватели"
            // у всех админов и колонки с ФИО на других вкладках должны обновиться.
            string entity = existingUser.Role == "Teacher" ? "Teacher" : "Student";
            await notify.BroadcastDataChangedAsync(entity, "Updated", id);
            return Results.Ok();
        });

        app.MapDelete("/api/users/{id:int}", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var target = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id AND Role = 'Student'", new { Id = id });
            if (target == null)
                return Common.ApiResults.Gone("UserDeleted",
                    "Этот студент уже был удалён другим администратором.");

            await notify.EndAllSessionsForUserAsync(id, "AccountDeleted",
                $"Ваша учётная запись «{target.Login}» была удалена администратором. Все ваши работы также удалены. Войти в систему больше нельзя.");

            await db.ExecuteAsync("DELETE FROM Users WHERE Id = @Id AND Role = 'Student'", new { Id = id });

            // Push-обновление: вкладки "Студенты", "Студенты и оценки", фильтры по группе — обновятся.
            await notify.BroadcastDataChangedAsync("Student", "Deleted", id);
            return Results.Ok();
        });
    }

    // Внутри Turing_Backend/Endpoints/AdminEndpoints.cs — заменить только этот метод.
    // Внутри Turing_Backend/Endpoints/AdminEndpoints.cs — заменить только этот метод.
    private static async Task<IResult> UpdateCourseInternal(int id, UpdateCourseDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify)
    {
        var role = ctx.Items["Role"] as string;
        if (role != "Teacher" && role != "Admin") return Results.Forbid();

        int currentUserId = (int)ctx.Items["UserId"]!;
        using var db = dbFactory.Create();

        var existing = await db.QueryFirstOrDefaultAsync<Course>(
            "SELECT * FROM Courses WHERE Id = @Id", new { Id = id });
        if (existing == null)
            return Common.ApiResults.Gone("CourseDeleted",
                "Курс, который вы редактировали, был удалён другим пользователем. Список курсов будет обновлён.");

        if (existing.Version != dto.Version)
            return Common.ApiResults.Conflict("VersionConflict",
                $"Курс «{existing.Name}» был изменён другим пользователем, пока вы его редактировали. Ваши изменения не сохранены — окно будет закрыто, откройте его заново для актуальных данных.",
                existing);

        if (role == "Teacher" && existing.TeacherId != currentUserId) return Results.Forbid();

        var (ok, err) = ValidationRules.ValidateCourseName(dto.Name);
        if (!ok) return Results.BadRequest(new { Error = err });

        string normalized = dto.Name.Trim();
        var dupCourse = await db.QueryFirstOrDefaultAsync<Course>(
            "SELECT * FROM Courses WHERE LOWER(Name) = LOWER(@Name) AND Id <> @Id",
            new { Name = normalized, Id = id });
        if (dupCourse != null)
            return Common.ApiResults.Conflict("DuplicateName",
                $"Курс с названием «{normalized}» уже существует в системе. Выберите другое название.");

        int normalizedArchived = dto.Archived == 1 ? 1 : 0;
        int newTeacherId = existing.TeacherId;
        if (role == "Admin" && dto.TeacherId.HasValue && dto.TeacherId.Value != existing.TeacherId)
        {
            var newTeacher = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id AND (Role = 'Teacher' OR Role = 'Admin')",
                new { Id = dto.TeacherId.Value });
            if (newTeacher == null)
                return Results.BadRequest(new { Error = "Указанный пользователь не является преподавателем." });
            newTeacherId = dto.TeacherId.Value;
        }

        int affected;
        try
        {
            affected = await db.ExecuteAsync(
                "UPDATE Courses SET Name = @Name, Description = @Description, TeacherId = @TeacherId, Archived = @Archived, Version = Version + 1, UpdatedAt = NOW() " +
                "WHERE Id = @Id AND Version = @Version",
                new { Name = normalized, dto.Description, TeacherId = newTeacherId, Archived = normalizedArchived, Id = id, dto.Version });
        }
        catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
        {
            return Common.ApiResults.Conflict("DuplicateName",
                $"Курс с названием «{normalized}» уже существует в системе. Выберите другое название.");
        }
        if (affected == 0)
        {
            var fresh = await db.QueryFirstOrDefaultAsync<Course>("SELECT * FROM Courses WHERE Id = @Id", new { Id = id });
            if (fresh == null)
                return Common.ApiResults.Gone("CourseDeleted",
                    "Курс был удалён другим пользователем в момент сохранения.");
            return Common.ApiResults.Conflict("VersionConflict",
                $"Курс «{fresh.Name}» был изменён другим пользователем в момент сохранения. Окно будет закрыто, откройте его заново для актуальных данных.", fresh);
        }

        // НОВАЯ ЛОГИКА: при архивации курса задания не закрываются автоматически —
        // они остаются с теми же статусами (но не показываются студентам, потому что
        // курс архивный). Преподаватель сам решит, нужно ли их перевести в "Архив".
        // Уведомляем студентов, у которых открыто задание этого курса.
        if (existing.Archived == 0 && normalizedArchived == 1)
        {
            var assignmentIds = await db.QueryAsync<int>(
                "SELECT Id FROM Assignments WHERE CourseId = @CourseId AND Status = 'Опубликовано'",
                new { CourseId = id });
            foreach (var aid in assignmentIds)
            {
                await notify.NotifyAssignmentChangedAsync(aid, "CourseArchived",
                    "Курс этого задания был отправлен в архив. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения.");
            }
        }

        await notify.BroadcastDataChangedAsync("Course", "Updated", id);
        return Results.Ok();
    }


}
