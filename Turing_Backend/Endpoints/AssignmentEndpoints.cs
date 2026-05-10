// Turing_Backend/Endpoints/AssignmentEndpoints.cs
using Dapper;
using Turing_Backend.Common;
using Turing_Backend.Database;
using Turing_Backend.Dtos;
using Turing_Backend.Hubs;
using Turing_Backend.Models;
using Turing_Backend.Services;

namespace Turing_Backend.Endpoints;

public static class AssignmentEndpoints
{
    public static void MapAssignmentEndpoints(this WebApplication app)
    {
        static bool IsTeacherOrAdmin(HttpContext ctx)
        {
            var role = ctx.Items["Role"] as string;
            return role == "Teacher" || role == "Admin";
        }

        // НОВАЯ БИЗНЕС-ЛОГИКА: у задания только два статуса — "Опубликовано" и "Архив".
        // Переходы разрешены в любую сторону без ограничений.
        // "Черновик" и "Скрыто" больше не используются (для совместимости со старой
        // БД они мигрируются в "Архив" при инициализации).
        static bool IsValidAssignmentStatus(string status)
        {
            return status == "Опубликовано" || status == "Архив";
        }

        app.MapGet("/api/assignments/my", async (HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            int userId = (int)ctx.Items["UserId"]!;
            string role = (string)ctx.Items["Role"]!;
            using var db = dbFactory.Create();

            if (role == "Teacher")
            {
                var assignments = await db.QueryAsync<Assignment>(@"
                    SELECT a.Id, a.Title, a.Type, a.Deadline, a.Status, a.Description, a.IsLocked, a.ConfigVersion, a.Version, a.CourseId, c.Name as CourseName
                    FROM Assignments a
                    JOIN Courses c ON a.CourseId = c.Id
                    WHERE c.TeacherId = @userId
                    ORDER BY a.Deadline", new { userId });
                return Results.Ok(assignments);
            }
            else if (role == "Admin")
            {
                var assignments = await db.QueryAsync<Assignment>(@"
                    SELECT a.Id, a.Title, a.Type, a.Deadline, a.Status, a.Description, a.IsLocked, a.ConfigVersion, a.Version, a.CourseId, c.Name as CourseName
                    FROM Assignments a
                    JOIN Courses c ON a.CourseId = c.Id
                    ORDER BY a.Deadline");
                return Results.Ok(assignments);
            }
            else
            {
                // Студент видит только опубликованные задания. Архивные не показываются.
                // SubmissionStatus теперь принимает значения "Не сдано" / "Не оценено" / "Оценено".
                // Если строки в Submissions нет — статус "Не сдано".
                var assignments = await db.QueryAsync<Assignment>(@"
                    SELECT DISTINCT a.Id, a.Title, a.Type, a.Deadline, a.Status, a.Description, a.Version, a.ConfigVersion,
                                    c.Name as CourseName, a.CourseId,
                                    COALESCE(s.Status, 'Не сдано') as SubmissionStatus
                    FROM Assignments a
                    JOIN Courses c ON a.CourseId = c.Id AND c.Archived = 0
                    JOIN Users u ON u.Id = @userId
                    JOIN CourseGroups cg ON c.Id = cg.CourseId AND cg.GroupId = u.GroupId
                    LEFT JOIN Submissions s ON a.Id = s.AssignmentId AND s.StudentId = u.Id
                    WHERE a.Status = 'Опубликовано'
                    ORDER BY a.Deadline", new { userId });
                return Results.Ok(assignments);
            }
        });

        app.MapPost("/api/assignments", async (CreateAssignmentDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();

            var (titleOk, titleErr) = Common.ValidationRules.ValidateAssignmentTitle(dto.Title);
            if (!titleOk) return Results.BadRequest(new { Error = titleErr });

            // Принимаем только "Опубликовано" / "Архив". Если клиент по старой памяти прислал
            // "Черновик" — нормализуем его в "Архив" (фактически "не активно").
            string normalizedStatus = dto.Status;
            if (normalizedStatus == "Черновик" || normalizedStatus == "Скрыто")
                normalizedStatus = "Архив";

            if (!IsValidAssignmentStatus(normalizedStatus))
                return Results.BadRequest(new { Error = "Допустимые статусы задания: «Опубликовано» или «Архив»." });

            using var db = dbFactory.Create();

            var courseExists = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM Courses WHERE Id = @CourseId", new { dto.CourseId });
            if (courseExists == 0)
                return Common.ApiResults.Gone("CourseDeleted",
                    "Курс, в котором вы создаёте задание, был удалён другим пользователем. Окно будет закрыто, обновите список курсов.");

            string trimmedTitle = dto.Title.Trim();
            var dupTask = await db.QueryFirstOrDefaultAsync<Assignment>(
                "SELECT * FROM Assignments WHERE CourseId = @CourseId AND LOWER(Title) = LOWER(@Title)",
                new { dto.CourseId, Title = trimmedTitle });
            if (dupTask != null)
                return Common.ApiResults.Conflict("DuplicateName",
                    $"В этом курсе уже есть задание с названием «{trimmedTitle}». Выберите другое название.");

            int newId;
            try
            {
                newId = await db.ExecuteScalarAsync<int>(
                    "INSERT INTO Assignments (Title, Type, Deadline, Status, CourseId, Description, ConfigVersion) " +
                    "VALUES (@Title, @Type, @Deadline, @Status, @CourseId, @Description, 1) RETURNING Id",
                    new { Title = trimmedTitle, dto.Type, dto.Deadline, Status = normalizedStatus, dto.CourseId, dto.Description });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                // Гонка параллельных вставок с одинаковым названием в одном курсе —
                // UNIQUE-индекс ux_assignments_course_title_ci пропустит только первую.
                return Common.ApiResults.Conflict("DuplicateName",
                    $"В этом курсе уже есть задание с названием «{trimmedTitle}». Выберите другое название.");
            }

            await notify.BroadcastDataChangedAsync("Assignment", "Created", newId);
            return Results.Ok();
        });

        app.MapPut("/api/assignments/{id:int}", async (int id, CreateAssignmentDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var assignment = await db.QueryFirstOrDefaultAsync<Assignment>(
                "SELECT * FROM Assignments WHERE Id = @Id", new { Id = id });
            if (assignment == null)
                return Common.ApiResults.Gone("AssignmentDeleted",
                    "Это задание было удалено другим пользователем — изменения сохранить нельзя. Список заданий будет обновлён.");

            if (assignment.Version != dto.Version)
            {
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Задание «{assignment.Title}» было изменено другим пользователем, пока вы его редактировали. Ваши изменения не сохранены — окно будет закрыто, откройте его заново для актуальных данных.",
                    assignment);
            }

            var (titleOk, titleErr) = Common.ValidationRules.ValidateAssignmentTitle(dto.Title);
            if (!titleOk) return Results.BadRequest(new { Error = titleErr });

            string newStatus = dto.Status;
            if (newStatus == "Черновик" || newStatus == "Скрыто")
                newStatus = "Архив";

            if (!IsValidAssignmentStatus(newStatus))
                return Results.BadRequest(new { Error = "Допустимые статусы задания: «Опубликовано» или «Архив»." });

            string newTitle = dto.Title.Trim();
            string newType = dto.Type;
            DateTime newDeadline = dto.Deadline;
            string? newDescription = dto.Description;

            // Определяем изменилась ли МТ-конфигурация
            bool mtChanged = !string.Equals(newDescription ?? "", assignment.Description ?? "", StringComparison.Ordinal);
            int newConfigVersion = assignment.ConfigVersion + (mtChanged ? 1 : 0);

            // Проверка дубликата названия в курсе.
            if (!string.Equals(newTitle, assignment.Title, StringComparison.OrdinalIgnoreCase))
            {
                var dupTask = await db.QueryFirstOrDefaultAsync<Assignment>(
                    "SELECT * FROM Assignments WHERE CourseId = @CourseId AND LOWER(Title) = LOWER(@Title) AND Id <> @Id",
                    new { CourseId = assignment.CourseId, Title = newTitle, Id = id });
                if (dupTask != null)
                    return Common.ApiResults.Conflict("DuplicateName",
                        $"В этом курсе уже есть задание с названием «{newTitle}». Выберите другое название.");
            }

            // Если МТ изменилась — пометить все Submissions как устаревшие
            if (mtChanged)
            {
                await db.ExecuteAsync(
                    "UPDATE Submissions SET IsOutdated = 1, AssignmentConfigVersion = @cv " +
                    "WHERE AssignmentId = @id AND Status NOT IN ('Оценено')",
                    new { cv = assignment.ConfigVersion, id });
            }

            int affected;
            try
            {
                affected = await db.ExecuteAsync(
                    "UPDATE Assignments SET Title = @Title, Type = @Type, Deadline = @Deadline, " +
                    "Status = @Status, Description = @Description, ConfigVersion = @ConfigVersion, " +
                    "Version = Version + 1, UpdatedAt = NOW() " +
                    "WHERE Id = @Id AND Version = @Version",
                    new
                    {
                        Title = newTitle,
                        Type = newType,
                        Deadline = newDeadline,
                        Status = newStatus,
                        Description = newDescription,
                        ConfigVersion = newConfigVersion,
                        Id = id,
                        dto.Version
                    });
            }
            catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
            {
                return Common.ApiResults.Conflict("DuplicateName",
                    $"В этом курсе уже есть задание с названием «{newTitle}». Выберите другое название.");
            }

            if (affected == 0)
            {
                var fresh = await db.QueryFirstOrDefaultAsync<Assignment>("SELECT * FROM Assignments WHERE Id = @Id", new { Id = id });
                if (fresh == null)
                    return Common.ApiResults.Gone("AssignmentDeleted",
                        "Задание было удалено другим пользователем в момент сохранения.");
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Задание «{fresh.Title}» было изменено другим пользователем в момент сохранения. Окно будет закрыто, откройте его заново для актуальных данных.",
                    fresh);
            }

            // Уведомления студентам, у которых задание открыто
            if (assignment.Status == "Опубликовано" && newStatus == "Архив")
            {
                await notify.NotifyAssignmentChangedAsync(id, "AssignmentArchived",
                    "Задание было переведено преподавателем в архив и сейчас недоступно для сдачи. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения.");
            }
            else if (assignment.Status == "Архив" && newStatus == "Опубликовано")
            {
                // Не критично уведомлять — задание просто появится в списке.
            }

            // Если изменился дедлайн — это важная вещь для студентов, у которых открыто задание.
            if (assignment.Deadline != newDeadline && newStatus == "Опубликовано")
            {
                await notify.NotifyAssignmentChangedAsync(id, "DeadlineChanged",
                    $"Преподаватель изменил срок сдачи задания «{newTitle}». Новый срок: {newDeadline:dd.MM.yyyy HH:mm}. Откройте задание заново, чтобы увидеть актуальное состояние.");
            }

            // Уведомление студентам об изменении МТ-конфигурации
            if (mtChanged && newStatus == "Опубликовано")
            {
                await notify.NotifyAssignmentChangedAsync(id, "MTUpdated",
                    $"Преподаватель обновил конфигурацию Машины Тьюринга в задании «{newTitle}». Ваше решение основано на старой версии МТ.");
            }

            await notify.BroadcastDataChangedAsync("Assignment", "Updated", id);
            return Results.Ok();
        });

        app.MapDelete("/api/assignments/{id:int}", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            var rows = await db.ExecuteAsync("DELETE FROM Assignments WHERE Id = @Id", new { Id = id });
            if (rows == 0)
                return Common.ApiResults.Gone("AssignmentDeleted",
                    "Это задание уже было удалено другим пользователем.");

            await notify.NotifyAssignmentChangedAsync(id, "AssignmentDeleted",
                "Задание было удалено преподавателем. Сохраните МТ через «Файл → Экспорт программы» и покиньте окно выполнения.");
            await notify.BroadcastDataChangedAsync("Assignment", "Deleted", id);
            return Results.Ok();
        });

        app.MapGet("/api/assignments/{assignmentId:int}/submissions", async (int assignmentId, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();
            // Студенты, чьи решения сейчас в статусе "Не сдано" или вообще без записи в Submissions,
            // не возвращаются — преподавателю не на что смотреть, пока студент ничего не сдал.
            var subs = await db.QueryAsync(@"
                SELECT s.Id, s.SolutionJson, s.SubmittedAt, s.Grade, s.TeacherComment, s.Status, s.Version,
                       s.IsBeingChecked, s.CheckStartedAt, s.IsOutdated, s.AssignmentConfigVersion,
                       u.FullName as StudentName, g.Name as GroupName
                FROM Submissions s
                JOIN Users u ON s.StudentId = u.Id
                LEFT JOIN Groups g ON u.GroupId = g.Id
                WHERE s.AssignmentId = @assignmentId AND s.Status IN ('Не оценено', 'Оценено')", new { assignmentId });
            return Results.Ok(subs);
        });

        app.MapGet("/api/assignments/{id:int}/state", async (int id, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            using var db = dbFactory.Create();
            var assignment = await db.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT a.Id, a.Title, a.Type, a.Deadline, a.Status, a.Description, a.Version, a.ConfigVersion, a.CourseId,
                       c.Name as CourseName, c.Archived as CourseArchived
                FROM Assignments a
                JOIN Courses c ON a.CourseId = c.Id
                WHERE a.Id = @Id", new { Id = id });
            if (assignment == null)
                return Common.ApiResults.Gone("AssignmentDeleted", "Задание удалено.");
            return Results.Ok(new
            {
                Id = (int)assignment.id,
                Title = (string)assignment.title,
                Type = (string)assignment.type,
                Deadline = (DateTime)assignment.deadline,
                Status = (string)assignment.status,
                Description = (string?)assignment.description,
                Version = (int)assignment.version,
                ConfigVersion = (int)assignment.configversion,
                CourseId = (int)assignment.courseid,
                CourseName = (string)assignment.coursename,
                CourseArchived = (int)assignment.coursearchived,
                IsDeleted = false
            });
        });

        // ==================== СТУДЕНТ ====================

        app.MapGet("/api/assignments/{id:int}/mysubmission", async (int id, HttpContext ctx, DbConnectionFactory dbFactory) =>
        {
            int studentId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();
            var sub = await db.QueryFirstOrDefaultAsync<Submission>(
                "SELECT * FROM Submissions WHERE AssignmentId = @id AND StudentId = @studentId",
                new { id, studentId });
            return Results.Ok(sub);
        });

        // /draft теперь — это ВСЕГДА сохранение/обновление РАБОЧЕГО решения студента.
        // Если у студента ещё нет записи — создаём её со статусом "Не оценено" (т.е.
        // решение «загружено», но ещё не сдано в смысле финальной отправки).
        // Согласно новой логике задачи, отдельного «черновика» нет — любое сохранение
        // считается загруженным решением. Однако клиентский код пока оставляет
        // возможность «В черновик» — мы трактуем это как «сохранить текущую версию,
        // оставаясь в статусе, позволяющем редактировать» (т.е. не финализировать).
        // Чтобы не ломать существующий REST-контракт, оставляем этот эндпоинт,
        // но статус решения теперь = "Не оценено" сразу.
        app.MapPost("/api/assignments/{id:int}/draft", async (int id, SubmitSubmissionDto dto, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            int studentId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var assignment = await db.QueryFirstOrDefaultAsync<Assignment>(
                "SELECT * FROM Assignments WHERE Id = @id", new { id });
            if (assignment == null)
                return Common.ApiResults.Gone("AssignmentDeleted",
                    "Это задание было удалено преподавателем. Сохранение решения невозможно. " +
                    "Рекомендуем экспортировать ваше решение в файл через «Файл → Экспорт программы», чтобы не потерять наработки.");

            if (assignment.Status != "Опубликовано")
                return Common.ApiResults.Conflict("AssignmentClosed",
                    $"Задание «{assignment.Title}» сейчас недоступно (находится в архиве). Сохранение решения невозможно.");

            var courseArchived = await db.ExecuteScalarAsync<int>(
                "SELECT COALESCE(MAX(Archived), 0) FROM Courses WHERE Id = @cid",
                new { cid = assignment.CourseId });
            if (courseArchived == 1)
                return Common.ApiResults.Conflict("CourseArchived",
                    "Курс этого задания был отправлен в архив. Сохранение работ по нему больше невозможно.");

            // Дедлайн: после дедлайна студент не может ни сохранять, ни редактировать.
            if (DateTime.Now >= assignment.Deadline)
                return Common.ApiResults.Conflict("DeadlineExpired",
                    $"Срок сдачи задания «{assignment.Title}» истёк {assignment.Deadline:dd.MM.yyyy HH:mm}. Изменение решения больше невозможно.");

            var existing = await db.QueryFirstOrDefaultAsync<Submission>(
                "SELECT * FROM Submissions WHERE AssignmentId = @id AND StudentId = @studentId",
                new { id, studentId });

            int submissionId;
            if (existing == null)
            {
                // Первое сохранение — статус сразу "Не оценено" (решение считается загруженным).
                // Гонка двух параллельных /draft была возможна раньше — теперь UNIQUE-индекс
                // ux_submissions_assignment_student гарантирует ровно одну запись. Если вставка
                // проиграла в гонке — достаём уже созданную запись и обновляем её решение.
                try
                {
                    submissionId = await db.ExecuteScalarAsync<int>(
                        "INSERT INTO Submissions (AssignmentId, StudentId, SolutionJson, SubmittedAt, Status, AssignmentConfigVersion, IsOutdated) " +
                        "VALUES (@id, @studentId, @json, @now, 'Не оценено', @cv, 0) RETURNING Id",
                        new { id, studentId, json = dto.SolutionJson, now = DateTime.Now, cv = assignment.ConfigVersion });
                }
                catch (Exception ex) when (PostgresErrorHelper.IsUniqueViolation(ex))
                {
                    var raced = await db.QueryFirstOrDefaultAsync<Submission>(
                        "SELECT * FROM Submissions WHERE AssignmentId = @id AND StudentId = @studentId",
                        new { id, studentId });
                    if (raced == null)
                        return Common.ApiResults.Conflict("SubmissionStateChanged",
                            "Не удалось сохранить решение из-за конкурентного изменения. Обновите окно и повторите.");
                    if (raced.Status == "Оценено" || raced.IsBeingChecked == 1)
                        return Common.ApiResults.Conflict("SubmissionLocked",
                            $"Работа по заданию «{assignment.Title}» уже находится в проверке или оценена. Изменение невозможно.");
                    bool racedBasedOnCurrent = dto.BasedOnConfigVersion.HasValue
                                            && dto.BasedOnConfigVersion.Value == assignment.ConfigVersion;
                    if (racedBasedOnCurrent)
                    {
                        await db.ExecuteAsync(
                            "UPDATE Submissions SET SolutionJson = @json, SubmittedAt = @now, Status = 'Не оценено', " +
                            "IsOutdated = 0, AssignmentConfigVersion = @cv, Version = Version + 1, UpdatedAt = NOW() WHERE Id = @subId",
                            new { json = dto.SolutionJson, now = DateTime.Now, cv = assignment.ConfigVersion, subId = raced.Id });
                    }
                    else
                    {
                        await db.ExecuteAsync(
                            "UPDATE Submissions SET SolutionJson = @json, SubmittedAt = @now, Status = 'Не оценено', " +
                            "Version = Version + 1, UpdatedAt = NOW() WHERE Id = @subId",
                            new { json = dto.SolutionJson, now = DateTime.Now, subId = raced.Id });
                    }
                    submissionId = raced.Id;
                }
            }
            else
            {
                // КЛЮЧЕВАЯ ПРОВЕРКА: если работа уже оценена — редактировать НЕЛЬЗЯ
                // (даже если дедлайн продлён). Это требование задачи.
                if (existing.Status == "Оценено")
                    return Common.ApiResults.Conflict("SubmissionLocked",
                        $"Работа по заданию «{assignment.Title}» уже оценена преподавателем. Изменение решения невозможно. Если преподаватель отзовёт оценку — вы сможете внести правки.");

                // Если решение в проверке — нельзя менять.
                if (existing.IsBeingChecked == 1)
                    return Common.ApiResults.Conflict("BeingChecked",
                        $"Работа по заданию «{assignment.Title}» сейчас находится на проверке у преподавателя. Дождитесь оценки или её отзыва — после этого вы сможете внести правки.");

                // Статус "Не сдано" или "Не оценено" — разрешаем редактирование.
                // При сохранении переводим в "Не оценено" (решение загружено).
                //
                // Подпись «Начальная МТ у задания была изменена» в окне информации
                // должна "залипать", пока студент продолжает редактировать своё
                // старое решение. НО если он сбросил решение к исходной (актуальной)
                // конфигурации преподавателя и сохраняет — флаг должен сняться.
                // Клиент сообщает об этом через BasedOnConfigVersion: значение
                // равно текущей Assignment.ConfigVersion ⇒ сохранение основано на
                // актуальной МТ ⇒ снимаем IsOutdated и обновляем AssignmentConfigVersion.
                // Если поле не задано или не совпадает — оставляем как было.
                bool basedOnCurrent = dto.BasedOnConfigVersion.HasValue
                                   && dto.BasedOnConfigVersion.Value == assignment.ConfigVersion;
                if (basedOnCurrent)
                {
                    await db.ExecuteAsync(
                        "UPDATE Submissions SET SolutionJson = @json, SubmittedAt = @now, Status = 'Не оценено', " +
                        "IsOutdated = 0, AssignmentConfigVersion = @cv, Version = Version + 1, UpdatedAt = NOW() WHERE Id = @subId",
                        new { json = dto.SolutionJson, now = DateTime.Now, cv = assignment.ConfigVersion, subId = existing.Id });
                }
                else
                {
                    await db.ExecuteAsync(
                        "UPDATE Submissions SET SolutionJson = @json, SubmittedAt = @now, Status = 'Не оценено', " +
                        "Version = Version + 1, UpdatedAt = NOW() WHERE Id = @subId",
                        new { json = dto.SolutionJson, now = DateTime.Now, subId = existing.Id });
                }
                submissionId = existing.Id;
            }

            await notify.BroadcastDataChangedAsync("Submission", existing == null ? "Created" : "Updated", submissionId);
            return Results.Ok();
        });

        // /submit фактически дублирует /draft в новой логике (решение считается сданным
        // сразу при сохранении). Оставляем для совместимости с клиентом — он может вызвать
        // /draft и затем /submit для финализации. Сервер просто проверяет, что решение
        // загружено и находится в статусе "Не оценено" (или переводит в "Не оценено").
        app.MapPost("/api/assignments/{id:int}/submit", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            int studentId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var assignment = await db.QueryFirstOrDefaultAsync<Assignment>(
                "SELECT * FROM Assignments WHERE Id = @id", new { id });
            if (assignment == null)
                return Common.ApiResults.Gone("AssignmentDeleted",
                    "Это задание было удалено преподавателем. Сдача невозможна.");

            if (assignment.Status != "Опубликовано")
                return Common.ApiResults.Conflict("AssignmentClosed",
                    $"Задание «{assignment.Title}» сейчас недоступно (находится в архиве). Сдача невозможна.");

            if (DateTime.Now >= assignment.Deadline)
                return Common.ApiResults.Conflict("DeadlineExpired",
                    $"Срок сдачи задания «{assignment.Title}» истёк {assignment.Deadline:dd.MM.yyyy HH:mm}. Сдача больше невозможна.");

            var existing = await db.QueryFirstOrDefaultAsync<Submission>(
                "SELECT * FROM Submissions WHERE AssignmentId = @id AND StudentId = @studentId",
                new { id, studentId });

            if (existing == null)
                return Common.ApiResults.Conflict("NoDraft",
                    $"Не удалось отправить работу по заданию «{assignment.Title}»: у вас нет сохранённого решения. Сначала сохраните решение.");

            if (existing.Status == "Оценено")
                return Common.ApiResults.Conflict("SubmissionLocked",
                    $"Работа по заданию «{assignment.Title}» уже оценена преподавателем — повторная отправка невозможна.");

            // Уже "Не оценено" — это уже отправленное решение, повторная отправка не нужна, но и не ошибка.
            if (existing.Status == "Не оценено")
            {
                return Results.Ok();
            }

            // Статус "Не сдано" — переводим в "Не оценено".
            int rows = await db.ExecuteAsync(
                "UPDATE Submissions SET Status = 'Не оценено', SubmittedAt = @now, Version = Version + 1, UpdatedAt = NOW() " +
                "WHERE Id = @subId AND Status = 'Не сдано'",
                new { subId = existing.Id, now = DateTime.Now });

            if (rows == 0)
            {
                return Common.ApiResults.Conflict("NoDraft",
                    $"Не удалось отправить работу по заданию «{assignment.Title}». Состояние работы изменилось — обновите окно.");
            }

            await notify.BroadcastDataChangedAsync("Submission", "Updated", id);
            return Results.Ok();
        });

        // /revoke — студент отзывает загруженное решение (переводит из "Не оценено" в "Не сдано").
        // НОВАЯ ЛОГИКА: разрешено только если:
        //   • Дедлайн ещё не истёк
        //   • Преподаватель ещё не оценил
        //   • Преподаватель сейчас не открыл работу на проверку (IsBeingChecked=0)
        //   • Задание опубликовано (не в архиве) и курс не архивирован
        app.MapPost("/api/assignments/{id:int}/revoke", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            int studentId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var assignment = await db.QueryFirstOrDefaultAsync<Assignment>(
                "SELECT * FROM Assignments WHERE Id = @id", new { id });
            if (assignment == null)
                return Common.ApiResults.Gone("AssignmentDeleted",
                    "Задание было удалено преподавателем. Отозвать работу невозможно.");

            if (assignment.Status != "Опубликовано")
                return Common.ApiResults.Conflict("AssignmentClosed",
                    $"Задание «{assignment.Title}» сейчас недоступно (находится в архиве). Отозвать работу нельзя.");

            if (DateTime.Now >= assignment.Deadline)
                return Common.ApiResults.Conflict("DeadlineExpired",
                    $"Срок сдачи задания «{assignment.Title}» истёк — отозвать работу больше нельзя.");

            var existing = await db.QueryFirstOrDefaultAsync<Submission>(
                "SELECT * FROM Submissions WHERE AssignmentId = @id AND StudentId = @studentId",
                new { id, studentId });

            if (existing == null)
                return Common.ApiResults.Conflict("NoSubmission",
                    $"По заданию «{assignment.Title}» нет загруженной работы — отзывать нечего.");

            if (existing.Status == "Оценено")
                return Common.ApiResults.Conflict("AlreadyChecked",
                    $"Преподаватель уже оценил вашу работу по заданию «{assignment.Title}». Отозвать её нельзя. Если преподаватель отзовёт оценку — вы сможете изменить решение.");

            if (existing.Status == "Не сдано")
                return Common.ApiResults.Conflict("AlreadyRevoked",
                    $"Работа по заданию «{assignment.Title}» уже находится в статусе «Не сдано» — отзывать нечего.");

            if (existing.IsBeingChecked == 1)
                return Common.ApiResults.Conflict("BeingChecked",
                    $"Преподаватель уже открыл проверку вашей работы по заданию «{assignment.Title}». Отозвать её нельзя — дождитесь оценки.");

            // Переводим в "Не сдано" — решение остаётся в БД (студент может его доредактировать),
            // но больше не считается отправленным на проверку.
            int rows = await db.ExecuteAsync(
                "UPDATE Submissions SET Status = 'Не сдано', Version = Version + 1, UpdatedAt = NOW() " +
                "WHERE Id = @subId AND Status = 'Не оценено' AND IsBeingChecked = 0",
                new { subId = existing.Id });

            if (rows == 0)
            {
                return Common.ApiResults.Conflict("AlreadyChecked",
                    $"Не удалось отозвать работу по заданию «{assignment.Title}». Возможно, преподаватель уже начал её проверку.");
            }

            await notify.NotifySubmissionChangedAsync(existing.Id, "Revoked",
                "Студент отозвал свою работу с проверки.");
            await notify.BroadcastDataChangedAsync("Submission", "Updated", existing.Id);
            return Results.Ok();
        });

        // Студент сбрасывает своё решение (удаляет submission) чтобы начать заново по новой МТ.
        // Разрешено только если: дедлайн не истёк, работа не оценена и не в проверке.
        app.MapPost("/api/assignments/{id:int}/reset", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            int studentId = (int)ctx.Items["UserId"]!;
            using var db = dbFactory.Create();

            var assignment = await db.QueryFirstOrDefaultAsync<Assignment>(
                "SELECT * FROM Assignments WHERE Id = @id", new { id });
            if (assignment == null)
                return Common.ApiResults.Gone("AssignmentDeleted",
                    "Задание было удалено преподавателем.");

            if (assignment.Status != "Опубликовано")
                return Common.ApiResults.Conflict("AssignmentClosed",
                    $"Задание «{assignment.Title}» сейчас недоступно (находится в архиве).");

            if (DateTime.Now >= assignment.Deadline)
                return Common.ApiResults.Conflict("DeadlineExpired",
                    $"Срок сдачи задания «{assignment.Title}» истёк — сбросить решение нельзя.");

            var existing = await db.QueryFirstOrDefaultAsync<Submission>(
                "SELECT * FROM Submissions WHERE AssignmentId = @id AND StudentId = @studentId",
                new { id, studentId });

            if (existing == null)
                return Results.Ok(); // Нечего сбрасывать

            if (existing.Status == "Оценено")
                return Common.ApiResults.Conflict("SubmissionLocked",
                    $"Работа по заданию «{assignment.Title}» уже оценена. Сбросить её нельзя.");

            if (existing.IsBeingChecked == 1)
                return Common.ApiResults.Conflict("BeingChecked",
                    $"Преподаватель сейчас проверяет вашу работу по заданию «{assignment.Title}». Сбросить её нельзя.");

            await db.ExecuteAsync(
                "DELETE FROM Submissions WHERE Id = @subId AND StudentId = @studentId",
                new { subId = existing.Id, studentId });

            await notify.BroadcastDataChangedAsync("Submission", "Deleted", existing.Id);
            return Results.Ok();
        });

        app.MapPost("/api/Submissions/{id:int}/start-check", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var sub = await db.QueryFirstOrDefaultAsync<Submission>(
                "SELECT * FROM Submissions WHERE Id = @Id", new { Id = id });
            if (sub == null)
                return Common.ApiResults.Gone("SubmissionDeleted",
                    "Эта работа больше не существует.");

            // Можно открыть на проверку либо "Не оценено", либо "Оценено" (для смены оценки/комментария).
            if (sub.Status != "Не оценено" && sub.Status != "Оценено")
                return Common.ApiResults.Conflict("SubmissionStateChanged",
                    $"Эту работу нельзя взять в проверку: её текущий статус «{sub.Status}».", sub);

            // НОВОЕ ПРАВИЛО: оценивать (брать в проверку для оценки) можно только после дедлайна.
            // НО: если работа уже оценена — открыть её для смены/отзыва оценки можно в любой момент.
            if (sub.Status == "Не оценено")
            {
                var assignment = await db.QueryFirstOrDefaultAsync<Assignment>(
                    "SELECT * FROM Assignments WHERE Id = @Id", new { Id = sub.AssignmentId });
                if (assignment == null)
                    return Common.ApiResults.Gone("AssignmentDeleted", "Задание этой работы было удалено.");

                if (DateTime.Now < assignment.Deadline)
                    return Common.ApiResults.Conflict("DeadlineNotPassed",
                        $"Оценивание работ по заданию «{assignment.Title}» возможно только после истечения срока сдачи ({assignment.Deadline:dd.MM.yyyy HH:mm}). Студент ещё может изменить своё решение.");
            }

            await db.ExecuteAsync(
                "UPDATE Submissions SET IsBeingChecked = 1, CheckStartedAt = @Now, UpdatedAt = NOW() WHERE Id = @Id",
                new { Now = DateTime.Now, Id = id });

            await notify.BroadcastDataChangedAsync("Submission", "Updated", id);
            return Results.Ok();
        });

        // Оценивание работы. НОВАЯ ЛОГИКА:
        //   • Можно оценивать только после дедлайна (если работа в "Не оценено").
        //   • Можно менять оценку/комментарий уже оценённой работы в любое время (без проверки дедлайна).
        app.MapPut("/api/Submissions/{id:int}", async (int id, GradeSubmissionDto payload, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var submission = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT s.*, u.FullName as studentname, a.Title as assignmenttitle, a.Deadline as assignmentdeadline
                  FROM Submissions s
                  JOIN Users u ON s.StudentId = u.Id
                  JOIN Assignments a ON s.AssignmentId = a.Id
                  WHERE s.Id = @Id", new { Id = id });
            if (submission == null)
                return Common.ApiResults.Gone("SubmissionDeleted",
                    "Эта работа больше не существует. Возможно, задание было удалено преподавателем.");

            string status = (string)submission.status;
            int version = (int)submission.version;
            string studentName = (string)submission.studentname;
            string assignmentTitle = (string)submission.assignmenttitle;
            DateTime deadline = (DateTime)submission.assignmentdeadline;

            // Оценивать можно "Не оценено" и "Оценено" (смена оценки).
            if (status != "Не оценено" && status != "Оценено")
            {
                string detail = status switch
                {
                    "Не сдано" => $"Студент «{studentName}» отозвал работу по заданию «{assignmentTitle}» — её нельзя оценить, пока он не загрузит решение снова.",
                    _ => $"Работа студента «{studentName}» по заданию «{assignmentTitle}» имеет статус «{status}» — её нельзя оценить."
                };
                return Common.ApiResults.Conflict("SubmissionStateChanged", detail, submission);
            }

            // Если работа ещё "Не оценено" — проверяем дедлайн.
            if (status == "Не оценено" && DateTime.Now < deadline)
            {
                return Common.ApiResults.Conflict("DeadlineNotPassed",
                    $"Оценивание работ по заданию «{assignmentTitle}» возможно только после истечения срока сдачи ({deadline:dd.MM.yyyy HH:mm}). До этого времени студент ещё может изменить своё решение.");
            }

            if (payload.Version != 0 && payload.Version != version)
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Состояние работы студента «{studentName}» по заданию «{assignmentTitle}» изменилось, пока вы её проверяли. Список работ будет обновлён.",
                    submission);

            // Доменная валидация оценки (CHECK chk_submissions_grade гарантирует 0..100,
            // но лучше вернуть пользователю понятный 400, чем ждать 500 от БД).
            if (payload.Grade.HasValue && (payload.Grade.Value < 0 || payload.Grade.Value > 100))
            {
                return Results.BadRequest(new { Error = "Оценка должна быть в диапазоне от 0 до 100." });
            }

            int affected = await db.ExecuteAsync(
                "UPDATE Submissions SET Grade = @Grade, TeacherComment = @TeacherComment, Status = 'Оценено', " +
                "IsBeingChecked = 0, CheckStartedAt = NULL, Version = Version + 1, UpdatedAt = NOW() " +
                "WHERE Id = @Id AND Version = @Version AND Status IN ('Не оценено', 'Оценено')",
                new
                {
                    Id = id,
                    Grade = payload.Grade,
                    TeacherComment = payload.TeacherComment,
                    Version = version
                });

            if (affected == 0)
            {
                var fresh = await db.QueryFirstOrDefaultAsync<Submission>("SELECT * FROM Submissions WHERE Id = @Id", new { Id = id });
                if (fresh == null)
                    return Common.ApiResults.Gone("SubmissionDeleted",
                        $"Работа студента «{studentName}» была удалена в момент сохранения оценки.");
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Состояние работы студента «{studentName}» по заданию «{assignmentTitle}» изменилось в момент сохранения оценки. Список работ будет обновлён.",
                    fresh);
            }

            await notify.BroadcastDataChangedAsync("Submission", "Updated", id);
            return Results.Ok();
        });

        app.MapPut("/api/Submissions/{id:int}/comment", async (int id, CommentOnlySubmissionDto payload, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var submission = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT s.*, u.FullName as studentname, a.Title as assignmenttitle
                  FROM Submissions s
                  JOIN Users u ON s.StudentId = u.Id
                  JOIN Assignments a ON s.AssignmentId = a.Id
                  WHERE s.Id = @Id", new { Id = id });
            if (submission == null)
                return Common.ApiResults.Gone("SubmissionDeleted",
                    "Эта работа больше не существует.");

            string status = (string)submission.status;
            int version = (int)submission.version;
            string studentName = (string)submission.studentname;
            string assignmentTitle = (string)submission.assignmenttitle;

            if (status != "Не оценено" && status != "Оценено")
            {
                return Common.ApiResults.Conflict("SubmissionStateChanged",
                    $"Нельзя добавить комментарий к работе студента «{studentName}» по заданию «{assignmentTitle}»: её текущий статус «{status}».",
                    submission);
            }

            if (payload.Version != 0 && payload.Version != version)
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Состояние работы студента «{studentName}» по заданию «{assignmentTitle}» изменилось. Список работ будет обновлён.",
                    submission);

            int affected = await db.ExecuteAsync(
                "UPDATE Submissions SET TeacherComment = @TeacherComment, IsBeingChecked = 0, CheckStartedAt = NULL, Version = Version + 1, UpdatedAt = NOW() " +
                "WHERE Id = @Id AND Version = @Version",
                new { Id = id, TeacherComment = payload.TeacherComment, Version = version });

            if (affected == 0)
            {
                var fresh = await db.QueryFirstOrDefaultAsync<Submission>("SELECT * FROM Submissions WHERE Id = @Id", new { Id = id });
                if (fresh == null)
                    return Common.ApiResults.Gone("SubmissionDeleted",
                        $"Работа студента «{studentName}» была удалена в момент сохранения комментария.");
                return Common.ApiResults.Conflict("VersionConflict",
                    $"Состояние работы студента «{studentName}» по заданию «{assignmentTitle}» изменилось. Список работ будет обновлён.",
                    fresh);
            }

            await notify.BroadcastDataChangedAsync("Submission", "Updated", id);
            return Results.Ok();
        });

        // Отзыв оценки. НОВАЯ ЛОГИКА:
        //   • Можно в любое время (даже если дедлайн ещё не истёк или уже истёк).
        //   • При отзыве оценка и комментарий обнуляются, статус → "Не оценено".
        //   • Студент сможет редактировать решение ТОЛЬКО если дедлайн ещё не истёк
        //     (это проверяется на стороне студента в /draft и /revoke).
        //     Если дедлайн истёк — студент не сможет править, а препод просто
        //     заново оценит то же решение.
        app.MapPost("/api/Submissions/{id:int}/unaccept", async (int id, HttpContext ctx, DbConnectionFactory dbFactory, NotificationService notify) =>
        {
            if (!IsTeacherOrAdmin(ctx)) return Results.Forbid();
            using var db = dbFactory.Create();

            var sub = await db.QueryFirstOrDefaultAsync<dynamic>(
                @"SELECT s.*, a.Title as assignmenttitle, a.Deadline as assignmentdeadline, u.FullName as studentname
                  FROM Submissions s
                  JOIN Assignments a ON s.AssignmentId = a.Id
                  JOIN Users u ON s.StudentId = u.Id
                  WHERE s.Id = @Id", new { Id = id });

            if (sub == null)
                return Common.ApiResults.Gone("SubmissionDeleted", "Эта работа больше не существует.");

            string status = (string)sub.status;
            string assignmentTitle = (string)sub.assignmenttitle;
            string studentName = (string)sub.studentname;

            if (status != "Оценено")
                return Common.ApiResults.Conflict("SubmissionStateChanged",
                    $"Работа студента «{studentName}» по заданию «{assignmentTitle}» сейчас не имеет статус «Оценено» — отзывать оценку нечего.", sub);

            // Сбрасываем оценку и комментарий, переводим в "Не оценено".
            int affected = await db.ExecuteAsync(
                "UPDATE Submissions SET Status = 'Не оценено', Grade = NULL, TeacherComment = NULL, IsBeingChecked = 0, CheckStartedAt = NULL, Version = Version + 1, UpdatedAt = NOW() " +
                "WHERE Id = @Id AND Status = 'Оценено'",
                new { Id = id });

            if (affected == 0)
                return Common.ApiResults.Conflict("SubmissionStateChanged",
                    $"Не удалось отозвать оценку для работы студента «{studentName}». Состояние работы изменилось — обновите список.", sub);

            await notify.NotifySubmissionChangedAsync(id, "GradeRevoked",
                "Преподаватель отозвал оценку вашей работы. Если срок сдачи ещё не истёк — вы можете внести правки.");
            await notify.BroadcastDataChangedAsync("Submission", "Updated", id);
            return Results.Ok();
        });
    }
}
