using Dapper;
using Turing_Backend.Common;
using Turing_Backend.Database;
using Turing_Backend.Dtos;
using Turing_Backend.Hubs;
using Turing_Backend.Models;
using Turing_Backend.Services;

namespace Turing_Backend.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var config = app.Services.GetRequiredService<IConfiguration>();
        int maxPerLogin = config.GetValue<int?>("Security:MaxFailedAttemptsPerLogin") ?? 5;
        int maxPerIp = config.GetValue<int?>("Security:MaxFailedAttemptsPerIp") ?? 20;
        int lockoutMinutes = config.GetValue<int?>("Security:LockoutMinutes") ?? 15;

        app.MapPost("/api/Auth/login", async (LoginRequest loginData, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            using var db = dbFactory.Create();
            string ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var failuresByLogin = await db.ExecuteScalarAsync<long>(
                @"SELECT COUNT(1) FROM LoginAttempts
                  WHERE Login = @Login AND Success = 0
                  AND AttemptAt > @Since",
                new { loginData.Login, Since = DateTime.Now.AddMinutes(-lockoutMinutes) });

            if (failuresByLogin >= maxPerLogin)
            {
                return Results.Json(new
                {
                    Reason = "TooManyAttempts",
                    Message = $"Слишком много неудачных попыток входа в эту учётную запись. Подождите {lockoutMinutes} минут и попробуйте снова."
                }, statusCode: 429);
            }

            var failuresByIp = await db.ExecuteScalarAsync<long>(
                @"SELECT COUNT(1) FROM LoginAttempts
                  WHERE IpAddress = @Ip AND Success = 0
                  AND AttemptAt > @Since",
                new { Ip = ip, Since = DateTime.Now.AddMinutes(-lockoutMinutes) });

            if (failuresByIp >= maxPerIp)
            {
                return Results.Json(new
                {
                    Reason = "TooManyAttempts",
                    Message = $"Слишком много неудачных попыток входа с вашего адреса. Подождите {lockoutMinutes} минут и попробуйте снова."
                }, statusCode: 429);
            }

            var user = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Login = @Login",
                new { loginData.Login });

            bool ok = user != null && BCrypt.Net.BCrypt.Verify(loginData.Password, user.Password);

            await db.ExecuteAsync(
                "INSERT INTO LoginAttempts (Login, IpAddress, AttemptAt, Success) VALUES (@Login, @Ip, @Now, @Success)",
                new { loginData.Login, Ip = ip, Now = DateTime.Now, Success = ok ? 1 : 0 });

            if (!ok)
            {
                return Results.Json(new
                {
                    Reason = "InvalidCredentials",
                    Message = "Неверный логин или пароль. Проверьте правильность ввода и повторите попытку."
                }, statusCode: 401);
            }

            var existingSession = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Token, CreatedAt, IpAddress FROM Sessions WHERE UserId = @UserId LIMIT 1",
                new { UserId = user!.Id });

            if (existingSession != null)
            {
                string existingIp = (existingSession.ipaddress as string) ?? "неизвестно";
                DateTime existingCreated = (DateTime)existingSession.createdat;

                return Results.Json(new
                {
                    Reason = "AlreadyLoggedIn",
                    Message = $"В учётную запись «{user.Login}» уже выполнен вход с другого устройства " +
                              $"(IP: {existingIp}, время входа: {existingCreated:dd.MM.yyyy HH:mm:ss}).\n\n" +
                              "Одновременная работа в одной учётной записи с разных устройств запрещена. " +
                              "Дождитесь, пока работа на другом устройстве будет завершена, либо обратитесь к администратору."
                }, statusCode: 409);
            }

            string groupName = "";
            if (user.GroupId.HasValue)
            {
                groupName = await db.ExecuteScalarAsync<string>(
                    "SELECT Name FROM Groups WHERE Id = @Id",
                    new { Id = user.GroupId }) ?? "";
            }

            var sessionToken = Guid.NewGuid().ToString("N");
            await db.ExecuteAsync(
                "INSERT INTO Sessions (Token, UserId, CreatedAt, LastActivityAt, IpAddress) VALUES (@Token, @UserId, @Now, @Now, @Ip)",
                new { Token = sessionToken, UserId = user.Id, Now = DateTime.Now, Ip = ip });

            await db.ExecuteAsync(
                "UPDATE Users SET LastLoginAt = @Now WHERE Id = @Id",
                new { Now = DateTime.Now, Id = user.Id });

            return Results.Ok(new
            {
                SessionId = sessionToken,
                Role = user.Role,
                FullName = user.FullName,
                Login = user.Login,
                Group = groupName,
                MustChangePassword = user.MustChangePassword == 1
            });
        });

        app.MapPost("/api/Auth/logout", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if (!ctx.Request.Headers.TryGetValue("X-Session-Id", out var token))
                return Results.Ok();

            using var db = dbFactory.Create();
            string tokenStr = token.ToString();
            await db.ExecuteAsync("DELETE FROM Sessions WHERE Token = @Token", new { Token = tokenStr });
            NotificationHub.RemoveSession(tokenStr);
            return Results.Ok();
        });

        app.MapGet("/api/Auth/me", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            int userId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var user = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = userId });

            if (user == null)
            {
                return Common.ApiResults.SessionEnded("UserDeleted",
                    "Ваша учётная запись была удалена администратором. Сохраните свою работу через «Файл → Экспорт программы», после чего войдите заново.");
            }

            string groupName = "";
            if (user.GroupId.HasValue)
            {
                groupName = await db.ExecuteScalarAsync<string>(
                    "SELECT Name FROM Groups WHERE Id = @Id",
                    new { Id = user.GroupId }) ?? "";
            }

            return Results.Ok(new
            {
                Id = user.Id,
                Login = user.Login,
                FullName = user.FullName,
                Role = user.Role,
                Group = groupName,
                MustChangePassword = user.MustChangePassword == 1
            });
        });

        app.MapPost("/api/Auth/change-password", async (ChangePasswordDto dto, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            int userId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var user = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = userId });
            if (user == null) return Results.NotFound();

            if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, user.Password))
                return Results.BadRequest(new { Error = "Текущий пароль введён неверно. Проверьте правильность ввода." });

            var (pwOk, pwErr) = ValidationRules.ValidatePassword(dto.NewPassword);
            if (!pwOk)
                return Results.BadRequest(new { Error = pwErr });

            if (dto.OldPassword == dto.NewPassword)
                return Results.BadRequest(new { Error = "Новый пароль должен отличаться от текущего." });

            var newHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await db.ExecuteAsync(
                "UPDATE Users SET Password = @Hash, MustChangePassword = 0, UpdatedAt = NOW() WHERE Id = @Id",
                new { Hash = newHash, Id = userId });

            return Results.Ok();
        });

        // ВАЖНО: студенты и преподаватели больше НЕ могут менять собственное ФИО.
        // Эндпоинт остаётся только для совместимости со старыми клиентами и возвращает 403.
        app.MapPut("/api/Auth/profile", (UpdateProfileDto dto, HttpContext ctx) =>
        {
            return Results.Json(new
            {
                Error = "Изменение ФИО доступно только администратору. Обратитесь к администратору для корректировки персональных данных."
            }, statusCode: 403);
        });

        // Полный метод POST /api/Admin/teachers — создание преподавателя.
        // Изменение: добавлен NotificationService notify в DI; возвращаем созданный Id;
        // после успешного INSERT шлём BroadcastDataChangedAsync, чтобы открытые окна
        // "Администрирование" → вкладка "Преподаватели" обновились автоматически у всех
        // админов, как если бы они нажали "🔄 Обновить".
        app.MapPost("/api/Admin/teachers", async (CreateTeacherDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if ((string)ctx.Items["Role"]! != "Admin") return Results.Forbid();

            var (nameOk, nameErr) = ValidationRules.ValidateFullName(dto.FullName);
            if (!nameOk)
                return Results.BadRequest(new { Error = nameErr });

            var (lgOk, lgErr) = ValidationRules.ValidateLogin(dto.Login);
            if (!lgOk)
                return Results.BadRequest(new { Error = lgErr });

            var (pwOk, pwErr) = ValidationRules.ValidatePassword(dto.Password);
            if (!pwOk)
                return Results.BadRequest(new { Error = pwErr });

            using var db = dbFactory.Create();
            string trimmedLogin = dto.Login.Trim();
            // Проверка дубликата логина: возвращаем 409 с Reason="DuplicateName",
            // чтобы клиент показал конкретное сообщение о занятом логине, а не общее
            // окно «данные устарели».
            var existing = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE LOWER(Login) = LOWER(@Login)", new { Login = trimmedLogin });
            if (existing != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");

            var hash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            int newId;
            try
            {
                newId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Users (Login, Password, FullName, Role, MustChangePassword) VALUES (@Login, @Hash, @Name, 'Teacher', 1) RETURNING Id",
                    new { Login = trimmedLogin, Hash = hash, Name = ValidationRules.NormalizeFullName(dto.FullName) });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");
            }

            // Push-обновление: вкладка "Преподаватели" в окне "Администрирование",
            // фильтр преподавателей в окне "Курсы" — должны автоматически пополниться
            // у всех админов в системе, без нажатия кнопки "🔄 Обновить".
            await notify.BroadcastDataChangedAsync("Teacher", "Created", newId);
            return Results.Ok(new { Id = newId });
        });



        app.MapGet("/api/Admin/teachers", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if ((string)ctx.Items["Role"]! != "Admin") return Results.Forbid();
            using var db = dbFactory.Create();
            var teachers = await db.QueryAsync<User>(
                "SELECT Id, Login, FullName, Role, LastLoginAt, Version FROM Users WHERE Role = 'Teacher' ORDER BY FullName");
            return Results.Ok(teachers);
        });

        // Полный метод DELETE /api/Admin/teachers/{id} — удаление преподавателя.
        // Изменение: после успешного DELETE шлём BroadcastDataChangedAsync("Teacher","Deleted",id),
        // чтобы вкладка "Преподаватели" у всех админов обновилась мгновенно. Кроме того,
        // удаление преподавателя обнуляет TeacherId в его курсах — поэтому также шлём
        // "Course","Updated" для каждого затронутого курса (или общий broadcast).
        app.MapDelete("/api/Admin/teachers/{id:int}", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if ((string)ctx.Items["Role"]! != "Admin") return Results.Forbid();
            int currentUserId = (int)ctx.Items["UserId"]!;
            if (currentUserId == id)
                return Results.BadRequest(new { Error = "Нельзя удалить собственную учётную запись." });

            using var db = dbFactory.Create();

            var target = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id AND Role = 'Teacher'",
                new { Id = id });
            if (target == null)
                return Common.ApiResults.Gone("UserDeleted",
                    "Преподаватель уже был удалён другим администратором.");

            await notify.EndAllSessionsForUserAsync(id, "AccountDeleted",
                $"Ваша учётная запись «{target.Login}» была удалена администратором. " +
                "Войти в систему больше нельзя.");

            await db.ExecuteAsync("DELETE FROM Users WHERE Id = @Id AND Role = 'Teacher'", new { Id = id });

            // Push-обновление: вкладки "Преподаватели" и "Курсы" у всех админов обновятся
            // автоматически — список преподавателей сократится, у курсов исчезнет ФИО автора.
            await notify.BroadcastDataChangedAsync("Teacher", "Deleted", id);
            await notify.BroadcastDataChangedAsync("Course", "Updated", 0);
            return Results.Ok();
        });


        // Полный метод PUT /api/Admin/teachers/{id} — изменение преподавателя.
        // Изменение: после успешного UPDATE шлём BroadcastDataChangedAsync("Teacher","Updated",id),
        // чтобы у всех админов вкладка "Преподаватели" и фильтры по преподавателю в "Курсах"
        // обновились автоматически (новое ФИО / новый логин).
        app.MapPut("/api/Admin/teachers/{id:int}", async (int id, UpdateTeacherDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if ((string)ctx.Items["Role"]! != "Admin") return Results.Forbid();

            var (nameOk, nameErr) = ValidationRules.ValidateFullName(dto.FullName);
            if (!nameOk)
                return Results.BadRequest(new { Error = nameErr });

            var (lgOk, lgErr) = ValidationRules.ValidateLogin(dto.Login);
            if (!lgOk)
                return Results.BadRequest(new { Error = lgErr });

            using var db = dbFactory.Create();

            var target = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id AND Role = 'Teacher'",
                new { Id = id });
            if (target == null) return Common.ApiResults.Gone("UserDeleted",
                "Преподаватель был удалён другим администратором. Список будет обновлён.");

            string trimmedLogin = dto.Login.Trim();
            string normalizedName = ValidationRules.NormalizeFullName(dto.FullName);

            if (!string.Equals(target.Login, trimmedLogin, StringComparison.OrdinalIgnoreCase))
            {
                // Проверка дубликата логина: разрешаем оставить прежний логин,
                // но не разрешаем занять чужой. На дубликат — 409 с DuplicateName.
                var conflict = await db.QueryFirstOrDefaultAsync<User>(
                    "SELECT * FROM Users WHERE LOWER(Login) = LOWER(@Login) AND Id <> @Id",
                    new { Login = trimmedLogin, Id = id });
                if (conflict != null)
                    return Common.ApiResults.Conflict("DuplicateName",
                        $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");
            }

            bool loginChanged = !string.Equals(target.Login, trimmedLogin, StringComparison.Ordinal);
            bool nameChanged = !string.Equals(target.FullName, normalizedName, StringComparison.Ordinal);

            try
            {
                await db.ExecuteAsync(
                    "UPDATE Users SET Login = @Login, FullName = @Name, Version = Version + 1, UpdatedAt = NOW() WHERE Id = @Id",
                    new { Login = trimmedLogin, Name = normalizedName, Id = id });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"Логин «{trimmedLogin}» уже занят другим пользователем. Выберите другой логин.");
            }

            if (loginChanged || nameChanged)
            {
                await notify.EndAllSessionsForUserAsync(id, "ProfileDataChanged",
                    "Данные вашего профиля были изменены. Сессия принудительно завершена.");
            }

            // Push-обновление таблиц "Преподаватели" и фильтра преподавателей в "Курсах"
            // у всех админов автоматически — без нажатия "🔄 Обновить".
            await notify.BroadcastDataChangedAsync("Teacher", "Updated", id);
            return Results.Ok(new { LoginChanged = loginChanged });
        });



        // Полный метод POST /api/Admin/reset-password/{userId} — сброс пароля админом/преподом.
        // Изменение: после успешного UPDATE и завершения сессии шлём BroadcastDataChangedAsync
        // для соответствующей сущности — таблицы "Студенты"/"Преподаватели" обновятся
        // (показывают LastLoginAt, и MustChangePassword=1 может отображаться значком).
        app.MapPost("/api/Admin/reset-password/{userId:int}", async (int userId, ResetPasswordDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            string role = (string)ctx.Items["Role"]!;
            if (role != "Admin" && role != "Teacher") return Results.Forbid();

            int currentUserId = (int)ctx.Items["UserId"]!;
            if (currentUserId == userId)
                return Results.BadRequest(new { Error = "Нельзя сбросить собственный пароль через окно администрирования. Используйте «Сменить пароль» в профиле." });

            var (pwOk, pwErr) = ValidationRules.ValidatePassword(dto.NewPassword);
            if (!pwOk)
                return Results.BadRequest(new { Error = pwErr });

            using var db = dbFactory.Create();
            var target = await db.QueryFirstOrDefaultAsync<User>(
                "SELECT * FROM Users WHERE Id = @Id", new { Id = userId });
            if (target == null) return Common.ApiResults.Gone("UserDeleted",
                "Пользователь был удалён другим администратором. Список будет обновлён.");

            if (role == "Teacher" && target.Role != "Student") return Results.Forbid();

            var hash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            await db.ExecuteAsync(
                "UPDATE Users SET Password = @Hash, MustChangePassword = 1, UpdatedAt = NOW() WHERE Id = @Id",
                new { Hash = hash, Id = userId });

            await notify.EndAllSessionsForUserAsync(userId, "ProfileDataChanged",
                "Данные вашего профиля были изменены. Сессия принудительно завершена.");

            // Push-обновление: таблицы "Студенты" / "Преподаватели" покажут актуальный LastLoginAt
            // (после повторного входа у пользователя). Само поле MustChangePassword не отображается
            // в таблицах, но broadcast делает поведение системы единообразным.
            string entity = target.Role == "Teacher" ? "Teacher" : "Student";
            await notify.BroadcastDataChangedAsync(entity, "Updated", userId);

            return Results.Ok();
        });


        app.MapGet("/api/Admin/user-session-status/{userId:int}", async (int userId, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            string role = (string)ctx.Items["Role"]!;
            if (role != "Admin" && role != "Teacher") return Results.Forbid();

            using var db = dbFactory.Create();
            var session = await db.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Token, LastActivityAt, CreatedAt, IpAddress FROM Sessions WHERE UserId = @UserId",
                new { UserId = userId });

            if (session == null)
                return Results.Ok(new { IsOnline = false });

            DateTime lastActivity = session.lastactivityat;

            return Results.Ok(new
            {
                IsOnline = true,
                LastActivityAt = lastActivity,
                IpAddress = session.ipaddress as string ?? "неизвестно"
            });
        });
    }
}
