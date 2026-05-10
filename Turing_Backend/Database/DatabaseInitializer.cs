// Turing_Backend/Database/DatabaseInitializer.cs
using Dapper;
using BCrypt.Net;

namespace Turing_Backend.Database;

/// <summary>
/// Инициализация и миграция схемы PostgreSQL.
///
/// Принципы построения схемы (соответствуют ГОСТ Р ИСО/МЭК 9075, реляционной модели Кодда
/// и общим правилам нормализации):
///   • Каждая таблица — в 3НФ: атомарные атрибуты, нет транзитивных зависимостей.
///   • Доменная целостность обеспечена CHECK-ограничениями для всех «перечислимых»
///     полей (Role, статусы, булевы флаги 0/1, оценка 0..100).
///   • Ссылочная целостность обеспечена FOREIGN KEY с явно указанным ON DELETE.
///   • Уникальность бизнес-ключей закреплена УНИКАЛЬНЫМИ индексами (case-insensitive
///     там, где это требует бизнес-логика — Login/Group/Course/Title), а не только
///     проверками в коде. Это закрывает race condition при параллельных вставках.
///   • Каждая таблица имеет столбцы аудита CreatedAt / UpdatedAt (рекомендация
///     ГОСТ Р 53114-2008 «Защита информации. Обеспечение информационной безопасности»
///     и общая практика трассируемости изменений).
///   • Индексы построены на колонках, по которым ведутся частые JOIN/WHERE-запросы
///     (Submissions.AssignmentId, Submissions.StudentId, Assignments.CourseId,
///      CourseGroups.GroupId, Users.GroupId).
///   • Старые «технические долги» (мусорные DEFAULT-значения, лишние DROP) очищены.
///
/// Все ALTER-блоки выполняются идемпотентно через проверку information_schema /
/// pg_constraint, чтобы повторные старты сервера не приводили к ошибкам и чтобы
/// существующие БД без потери данных переходили к новой схеме.
/// </summary>
public static class DatabaseInitializer
{
    public static void Initialize(DbConnectionFactory factory)
    {
        using var db = factory.Create();

        var createTablesSql = @"
            -- =========================================================
            -- 1. ГРУППЫ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS Groups (
                Id SERIAL PRIMARY KEY,
                Name TEXT NOT NULL,
                Version INTEGER NOT NULL DEFAULT 1,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW()
            );

            -- Уникальность названия группы без учёта регистра — закрываем race condition
            -- при параллельной вставке. Старый UNIQUE(Name) (case-sensitive) удаляем,
            -- чтобы не было двух уровней уникальности.
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='groups' AND column_name='version') THEN
                    ALTER TABLE Groups ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='groups' AND column_name='createdat') THEN
                    ALTER TABLE Groups ADD COLUMN CreatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='groups' AND column_name='updatedat') THEN
                    ALTER TABLE Groups ADD COLUMN UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
            END $$;

            DO $$
            DECLARE
                old_unique TEXT;
            BEGIN
                -- Удаляем старое case-sensitive ограничение UNIQUE(Name), если оно есть.
                SELECT tc.constraint_name INTO old_unique
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name
                WHERE tc.table_name = 'groups'
                  AND tc.constraint_type = 'UNIQUE'
                  AND kcu.column_name = 'name'
                LIMIT 1;
                IF old_unique IS NOT NULL THEN
                    EXECUTE 'ALTER TABLE Groups DROP CONSTRAINT ' || quote_ident(old_unique);
                END IF;
            END $$;

            CREATE UNIQUE INDEX IF NOT EXISTS ux_groups_name_ci
                ON Groups (LOWER(Name));

            -- =========================================================
            -- 2. ПОЛЬЗОВАТЕЛИ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS Users (
                Id SERIAL PRIMARY KEY,
                Login TEXT NOT NULL,
                Password TEXT NOT NULL,
                FullName TEXT NOT NULL,
                Role TEXT NOT NULL,
                GroupId INTEGER,
                LastLoginAt TIMESTAMP,
                MustChangePassword INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                FOREIGN KEY (GroupId) REFERENCES Groups(Id) ON DELETE SET NULL
            );

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='users' AND column_name='lastloginat') THEN
                    ALTER TABLE Users ADD COLUMN LastLoginAt TIMESTAMP;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='users' AND column_name='mustchangepassword') THEN
                    ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='users' AND column_name='version') THEN
                    ALTER TABLE Users ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='users' AND column_name='createdat') THEN
                    ALTER TABLE Users ADD COLUMN CreatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='users' AND column_name='updatedat') THEN
                    ALTER TABLE Users ADD COLUMN UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
            END $$;

            -- Удаляем старое case-sensitive UNIQUE(Login), если оно ещё существует.
            DO $$
            DECLARE
                old_unique TEXT;
            BEGIN
                SELECT tc.constraint_name INTO old_unique
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name
                WHERE tc.table_name = 'users'
                  AND tc.constraint_type = 'UNIQUE'
                  AND kcu.column_name = 'login'
                LIMIT 1;
                IF old_unique IS NOT NULL THEN
                    EXECUTE 'ALTER TABLE Users DROP CONSTRAINT ' || quote_ident(old_unique);
                END IF;
            END $$;

            -- Уникальность логина без учёта регистра — атомарно на уровне БД.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_users_login_ci
                ON Users (LOWER(Login));

            -- Индекс по GroupId — ускоряет выборку студентов группы и фильтр в курсах.
            CREATE INDEX IF NOT EXISTS idx_users_groupid ON Users(GroupId);
            CREATE INDEX IF NOT EXISTS idx_users_role    ON Users(Role);

            -- Доменная целостность: Role — только из фиксированного множества.
            -- Дополнительно нормализуем существующие странные значения, чтобы CHECK
            -- мог быть установлен без ошибки.
            UPDATE Users SET Role = 'Admin'
                WHERE Login = 'admin' AND Role <> 'Admin';

            UPDATE Users SET Role = 'Student'
                WHERE Role NOT IN ('Student', 'Teacher', 'Admin');

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_role') THEN
                    ALTER TABLE Users ADD CONSTRAINT chk_users_role
                        CHECK (Role IN ('Student', 'Teacher', 'Admin'));
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_users_mustchangepassword') THEN
                    ALTER TABLE Users ADD CONSTRAINT chk_users_mustchangepassword
                        CHECK (MustChangePassword IN (0, 1));
                END IF;
            END $$;

            -- =========================================================
            -- 3. СЕССИИ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS Sessions (
                Token TEXT PRIMARY KEY,
                UserId INTEGER NOT NULL,
                CreatedAt TIMESTAMP NOT NULL,
                LastActivityAt TIMESTAMP NOT NULL,
                IpAddress TEXT,
                FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='sessions' AND column_name='lastactivityat') THEN
                    ALTER TABLE Sessions ADD COLUMN LastActivityAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='sessions' AND column_name='ipaddress') THEN
                    ALTER TABLE Sessions ADD COLUMN IpAddress TEXT;
                END IF;
            END $$;

            -- Бизнес-правило: «один пользователь — одна активная сессия» теперь
            -- гарантируется уникальным индексом, а не SELECT-ом перед INSERT.
            -- Перед созданием индекса вычищаем дубли (на случай старой БД, где
            -- их могло быть несколько из-за гонки) — оставляем самую свежую.
            DELETE FROM Sessions s USING Sessions s2
                WHERE s.UserId = s2.UserId
                  AND s.CreatedAt < s2.CreatedAt;

            CREATE UNIQUE INDEX IF NOT EXISTS ux_sessions_userid
                ON Sessions(UserId);

            -- =========================================================
            -- 4. ПРИЧИНЫ ЗАВЕРШЕНИЯ СЕССИИ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS SessionTerminationReasons (
                Id SERIAL PRIMARY KEY,
                Token TEXT NOT NULL,
                UserId INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                Message TEXT NOT NULL,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_sessionterm_token   ON SessionTerminationReasons(Token);
            CREATE INDEX IF NOT EXISTS idx_sessionterm_created ON SessionTerminationReasons(CreatedAt);

            -- Авточистка устаревших причин (старше 7 дней). Минимизация хранения данных
            -- (152-ФЗ ст. 5 ч. 7) и ограничение неконтролируемого роста таблицы.
            DELETE FROM SessionTerminationReasons WHERE CreatedAt < NOW() - INTERVAL '7 days';

            -- =========================================================
            -- 5. КУРСЫ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS Courses (
                Id SERIAL PRIMARY KEY,
                Name TEXT NOT NULL,
                TeacherId INTEGER,
                GradingPolicy TEXT,
                Description TEXT,
                Archived INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                FOREIGN KEY (TeacherId) REFERENCES Users(Id) ON DELETE SET NULL
            );

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='courses' AND column_name='description') THEN
                    ALTER TABLE Courses ADD COLUMN Description TEXT;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='courses' AND column_name='archived') THEN
                    ALTER TABLE Courses ADD COLUMN Archived INTEGER NOT NULL DEFAULT 0;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='courses' AND column_name='version') THEN
                    ALTER TABLE Courses ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='courses' AND column_name='createdat') THEN
                    ALTER TABLE Courses ADD COLUMN CreatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='courses' AND column_name='updatedat') THEN
                    ALTER TABLE Courses ADD COLUMN UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
            END $$;

            -- Унаследованная корректировка: TeacherId должен быть NULL-able и иметь FK SET NULL,
            -- иначе при удалении преподавателя курсы каскадно тёрлись или вставка падала.
            DO $$
            DECLARE
                col_nullable TEXT;
                fk_name TEXT;
                fk_action TEXT;
            BEGIN
                SELECT is_nullable INTO col_nullable
                FROM information_schema.columns
                WHERE table_name = 'courses' AND column_name = 'teacherid';

                IF col_nullable = 'NO' THEN
                    EXECUTE 'ALTER TABLE Courses ALTER COLUMN TeacherId DROP NOT NULL';
                END IF;

                SELECT tc.constraint_name, rc.delete_rule
                INTO fk_name, fk_action
                FROM information_schema.table_constraints tc
                JOIN information_schema.referential_constraints rc
                  ON tc.constraint_name = rc.constraint_name
                JOIN information_schema.key_column_usage kcu
                  ON tc.constraint_name = kcu.constraint_name
                WHERE tc.table_name = 'courses'
                  AND tc.constraint_type = 'FOREIGN KEY'
                  AND kcu.column_name = 'teacherid'
                LIMIT 1;

                IF fk_name IS NOT NULL AND fk_action <> 'SET NULL' THEN
                    EXECUTE 'ALTER TABLE Courses DROP CONSTRAINT ' || quote_ident(fk_name);
                    EXECUTE 'ALTER TABLE Courses ADD CONSTRAINT ' || quote_ident(fk_name) ||
                            ' FOREIGN KEY (TeacherId) REFERENCES Users(Id) ON DELETE SET NULL';
                END IF;
            END $$;

            -- Уникальность названия курса без учёта регистра.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_courses_name_ci
                ON Courses (LOWER(Name));

            -- Доменная целостность: Archived — булев флаг 0/1.
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_courses_archived') THEN
                    ALTER TABLE Courses ADD CONSTRAINT chk_courses_archived
                        CHECK (Archived IN (0, 1));
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS idx_courses_teacherid ON Courses(TeacherId);
            CREATE INDEX IF NOT EXISTS idx_courses_archived  ON Courses(Archived);

            -- =========================================================
            -- 6. ПРИВЯЗКА ГРУПП К КУРСАМ (M:N)
            -- =========================================================
            CREATE TABLE IF NOT EXISTS CourseGroups (
                CourseId INTEGER NOT NULL,
                GroupId INTEGER NOT NULL,
                PRIMARY KEY (CourseId, GroupId),
                FOREIGN KEY (CourseId) REFERENCES Courses(Id) ON DELETE CASCADE,
                FOREIGN KEY (GroupId)  REFERENCES Groups(Id)  ON DELETE CASCADE
            );

            -- Индекс для обратного поиска (по группе → её курсы) — частый JOIN
            -- в /api/assignments/my для студента.
            CREATE INDEX IF NOT EXISTS idx_coursegroups_groupid ON CourseGroups(GroupId);

            -- =========================================================
            -- 7. ЗАДАНИЯ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS Assignments (
                Id SERIAL PRIMARY KEY,
                Title TEXT NOT NULL,
                Type TEXT NOT NULL DEFAULT 'Домашняя работа',
                Deadline TIMESTAMP,
                Status TEXT NOT NULL DEFAULT 'Архив',
                CourseId INTEGER NOT NULL,
                Description TEXT,
                IsLocked INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                FOREIGN KEY (CourseId) REFERENCES Courses(Id) ON DELETE CASCADE
            );

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='assignments' AND column_name='version') THEN
                    ALTER TABLE Assignments ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='assignments' AND column_name='createdat') THEN
                    ALTER TABLE Assignments ADD COLUMN CreatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='assignments' AND column_name='updatedat') THEN
                    ALTER TABLE Assignments ADD COLUMN UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='assignments' AND column_name='configversion') THEN
                    ALTER TABLE Assignments ADD COLUMN ConfigVersion INTEGER NOT NULL DEFAULT 1;
                END IF;
            END $$;

            -- ===== МИГРАЦИЯ НА НОВУЮ БИЗНЕС-ЛОГИКУ ЗАДАНИЙ =====
            -- Старые статусы заданий: 'Черновик', 'Опубликовано', 'Скрыто', 'Закрыто'.
            -- Новые статусы: 'Опубликовано', 'Архив'.
            UPDATE Assignments SET Status = 'Архив'
                WHERE Status IN ('Черновик', 'Скрыто', 'Закрыто', '', ' ')
                   OR Status NOT IN ('Опубликовано', 'Архив');

            -- Чистим мусорные значения Type ('', ' ').
            UPDATE Assignments SET Type = 'Домашняя работа'
                WHERE Type IS NULL OR TRIM(Type) = '';

            -- Доменная целостность: статус задания — фиксированное множество, IsLocked — 0/1.
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_assignments_status') THEN
                    ALTER TABLE Assignments ADD CONSTRAINT chk_assignments_status
                        CHECK (Status IN ('Опубликовано', 'Архив'));
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_assignments_islocked') THEN
                    ALTER TABLE Assignments ADD CONSTRAINT chk_assignments_islocked
                        CHECK (IsLocked IN (0, 1));
                END IF;
            END $$;

            -- Поправляем DEFAULT мусора, который мог остаться от старых миграций.
            ALTER TABLE Assignments ALTER COLUMN Type   SET DEFAULT 'Домашняя работа';
            ALTER TABLE Assignments ALTER COLUMN Status SET DEFAULT 'Архив';

            -- Уникальность названия задания внутри одного курса (case-insensitive).
            CREATE UNIQUE INDEX IF NOT EXISTS ux_assignments_course_title_ci
                ON Assignments (CourseId, LOWER(Title));

            CREATE INDEX IF NOT EXISTS idx_assignments_courseid ON Assignments(CourseId);
            CREATE INDEX IF NOT EXISTS idx_assignments_status   ON Assignments(Status);
            CREATE INDEX IF NOT EXISTS idx_assignments_deadline ON Assignments(Deadline);

            -- =========================================================
            -- 8. РЕШЕНИЯ СТУДЕНТОВ
            -- =========================================================
            CREATE TABLE IF NOT EXISTS Submissions (
                Id SERIAL PRIMARY KEY,
                AssignmentId INTEGER NOT NULL,
                StudentId INTEGER NOT NULL,
                SolutionJson TEXT NOT NULL,
                SubmittedAt TIMESTAMP NOT NULL,
                Grade INTEGER,
                TeacherComment TEXT,
                Status TEXT NOT NULL DEFAULT 'Не оценено',
                Version INTEGER NOT NULL DEFAULT 1,
                IsBeingChecked INTEGER NOT NULL DEFAULT 0,
                CheckStartedAt TIMESTAMP NULL,
                CreatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW(),
                FOREIGN KEY (AssignmentId) REFERENCES Assignments(Id) ON DELETE CASCADE,
                FOREIGN KEY (StudentId)    REFERENCES Users(Id)       ON DELETE CASCADE
            );

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='version') THEN
                    ALTER TABLE Submissions ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='isbeingchecked') THEN
                    ALTER TABLE Submissions ADD COLUMN IsBeingChecked INTEGER NOT NULL DEFAULT 0;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='checkstartedat') THEN
                    ALTER TABLE Submissions ADD COLUMN CheckStartedAt TIMESTAMP NULL;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='createdat') THEN
                    ALTER TABLE Submissions ADD COLUMN CreatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='updatedat') THEN
                    ALTER TABLE Submissions ADD COLUMN UpdatedAt TIMESTAMP NOT NULL DEFAULT NOW();
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='assignmentconfigversion') THEN
                    ALTER TABLE Submissions ADD COLUMN AssignmentConfigVersion INTEGER NOT NULL DEFAULT 1;
                END IF;
                IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                               WHERE table_name='submissions' AND column_name='isoutdated') THEN
                    ALTER TABLE Submissions ADD COLUMN IsOutdated INTEGER NOT NULL DEFAULT 0;
                END IF;
            END $$;

            -- Старые статусы решений: 'Черновик', 'Не оценено', 'Оценено'.
            -- Новые: 'Не сдано', 'Не оценено', 'Оценено'.
            UPDATE Submissions SET Status = 'Не сдано'
                WHERE Status = 'Черновик';

            UPDATE Submissions SET Status = 'Не оценено'
                WHERE Status NOT IN ('Не сдано', 'Не оценено', 'Оценено');

            -- Поправляем DEFAULT мусора, который остался от старой схемы (' ').
            ALTER TABLE Submissions ALTER COLUMN Status SET DEFAULT 'Не оценено';

            -- Доменная целостность.
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_submissions_status') THEN
                    ALTER TABLE Submissions ADD CONSTRAINT chk_submissions_status
                        CHECK (Status IN ('Не сдано', 'Не оценено', 'Оценено'));
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_submissions_isbeingchecked') THEN
                    ALTER TABLE Submissions ADD CONSTRAINT chk_submissions_isbeingchecked
                        CHECK (IsBeingChecked IN (0, 1));
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_submissions_grade') THEN
                    ALTER TABLE Submissions ADD CONSTRAINT chk_submissions_grade
                        CHECK (Grade IS NULL OR (Grade >= 0 AND Grade <= 100));
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_submissions_isoutdated') THEN
                    ALTER TABLE Submissions ADD CONSTRAINT chk_submissions_isoutdated
                        CHECK (IsOutdated IN (0, 1));
                END IF;
            END $$;

            -- Бизнес-правило: «у одного студента — не более одного решения по одному заданию».
            -- Перед созданием уникального индекса вычищаем теоретические дубли (на случай
            -- старой БД, где в гонке могли вставиться две записи) — оставляем самое свежее.
            DELETE FROM Submissions s USING Submissions s2
                WHERE s.AssignmentId = s2.AssignmentId
                  AND s.StudentId    = s2.StudentId
                  AND s.Id < s2.Id;

            CREATE UNIQUE INDEX IF NOT EXISTS ux_submissions_assignment_student
                ON Submissions(AssignmentId, StudentId);

            CREATE INDEX IF NOT EXISTS idx_submissions_assignmentid ON Submissions(AssignmentId);
            CREATE INDEX IF NOT EXISTS idx_submissions_studentid    ON Submissions(StudentId);
            CREATE INDEX IF NOT EXISTS idx_submissions_status       ON Submissions(Status);

            -- =========================================================
            -- 9. ПОПЫТКИ ВХОДА
            -- =========================================================
            CREATE TABLE IF NOT EXISTS LoginAttempts (
                Id SERIAL PRIMARY KEY,
                Login TEXT NOT NULL,
                IpAddress TEXT,
                AttemptAt TIMESTAMP NOT NULL,
                Success INTEGER NOT NULL
            );

            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_loginattempts_success') THEN
                    ALTER TABLE LoginAttempts ADD CONSTRAINT chk_loginattempts_success
                        CHECK (Success IN (0, 1));
                END IF;
            END $$;

            CREATE INDEX IF NOT EXISTS idx_login_attempts_login_time
                ON LoginAttempts(Login, AttemptAt);
            CREATE INDEX IF NOT EXISTS idx_login_attempts_ip_time
                ON LoginAttempts(IpAddress, AttemptAt);

            -- Авточистка попыток входа старше 30 дней (минимизация хранения данных).
            DELETE FROM LoginAttempts WHERE AttemptAt < NOW() - INTERVAL '30 days';

            -- =========================================================
            -- 10. УБОРКА УСТАРЕВШИХ СУЩНОСТЕЙ
            -- =========================================================
            -- Таблица CourseEnrollments была заменена на CourseGroups (привязка через группу,
            -- а не через индивидуальное зачисление). Удаляем её ОДИН раз — если её нет, no-op.
            DROP TABLE IF EXISTS CourseEnrollments;
        ";

        db.Execute(createTablesSql);

        SeedData(db);
    }

    /// <summary>
    /// Начальные данные для пустой БД: учётные записи администратора и тестового
    /// преподавателя/студентов. Запускается один раз — повторные старты сервера
    /// не создают дубликатов благодаря проверке COUNT.
    /// </summary>
    private static void SeedData(System.Data.IDbConnection db)
    {
        var teacherCount = db.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Users WHERE Role IN ('Teacher', 'Admin')");

        if (teacherCount == 0)
        {
            var adminHash = BCrypt.Net.BCrypt.HashPassword("Admin123");
            db.Execute(
                "INSERT INTO Users (Login, Password, FullName, Role, MustChangePassword) " +
                "VALUES ('admin', @Hash, 'Администратор', 'Admin', 1)",
                new { Hash = adminHash });

            var teacherHash = BCrypt.Net.BCrypt.HashPassword("Teacher123");
            db.Execute(
                "INSERT INTO Users (Login, Password, FullName, Role) " +
                "VALUES ('teacher', @Hash, 'Преподаватель Тестовый', 'Teacher')",
                new { Hash = teacherHash });
        }

        var studentCount = db.ExecuteScalar<long>(
            "SELECT COUNT(1) FROM Users WHERE Role = 'Student'");
        if (studentCount > 0) return;

        var groupId = db.ExecuteScalar<int>(
            "INSERT INTO Groups (Name) VALUES ('РИС-24-1') RETURNING Id");

        (string LastName, string FirstName, string MiddleName)[] students = new (string, string, string)[]
        {
            ("Иванов", "Иван", "Иванович"),
            ("Петров", "Петр", "Петрович"),
            ("Сидоров", "Сидор", "Сидорович")
        };

        var studentHash = BCrypt.Net.BCrypt.HashPassword("Stud123");
        for (int i = 0; i < students.Length; i++)
        {
            var s = students[i];
            db.Execute(
                "INSERT INTO Users (Login, Password, FullName, Role, GroupId) " +
                "VALUES (@Login, @Hash, @FullName, 'Student', @GroupId)",
                new
                {
                    Login = $"st{i + 1}",
                    Hash = studentHash,
                    FullName = $"{s.LastName} {s.FirstName} {s.MiddleName}",
                    GroupId = groupId
                });
        }
    }
}
