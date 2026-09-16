-- Seed data for the demo source database (docker-compose `source-db` service, or the
-- Aspire AppHost's `source-mysql` resource). Auto-run by the MySQL image on first
-- container start (mounted at /docker-entrypoint-initdb.d).
-- Backs the tasks in Sql/courses.sql and Sql/course_analytics.sql.
--
-- Self-contained CREATE/USE: docker-compose pre-selects a database via MYSQL_DATABASE,
-- but Aspire's MySql resource doesn't, so this can't rely on one being selected already.
CREATE DATABASE IF NOT EXISTS beetle_demo;
USE beetle_demo;

CREATE TABLE IF NOT EXISTS Users (
    UserId INT PRIMARY KEY AUTO_INCREMENT,
    FullName VARCHAR(150) NOT NULL,
    Email VARCHAR(200) NOT NULL UNIQUE,
    Role VARCHAR(20) NOT NULL DEFAULT 'learner',
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS Courses (
    CourseId INT PRIMARY KEY AUTO_INCREMENT,
    CourseName VARCHAR(200) NOT NULL,
    Category VARCHAR(100) NOT NULL,
    DurationHours DECIMAL(5,2) NOT NULL,
    Instructor VARCHAR(150) NOT NULL,
    Price DECIMAL(10,2) NOT NULL,
    IsActive TINYINT(1) NOT NULL DEFAULT 1,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS Lessons (
    LessonId INT PRIMARY KEY AUTO_INCREMENT,
    CourseId INT NOT NULL,
    Title VARCHAR(200) NOT NULL,
    SequenceNo INT NOT NULL,
    DurationMinutes INT NOT NULL,
    CONSTRAINT fk_lessons_course FOREIGN KEY (CourseId) REFERENCES Courses(CourseId)
);

CREATE TABLE IF NOT EXISTS Enrollments (
    EnrollmentId INT PRIMARY KEY AUTO_INCREMENT,
    UserId INT NOT NULL,
    CourseId INT NOT NULL,
    Status VARCHAR(20) NOT NULL DEFAULT 'active', -- active | completed | dropped
    EnrolledAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_enrollments_user FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT fk_enrollments_course FOREIGN KEY (CourseId) REFERENCES Courses(CourseId),
    UNIQUE KEY uq_enrollment (UserId, CourseId)
);

CREATE TABLE IF NOT EXISTS LessonCompletions (
    CompletionId INT PRIMARY KEY AUTO_INCREMENT,
    UserId INT NOT NULL,
    LessonId INT NOT NULL,
    Score DECIMAL(5,2) NULL,
    CompletedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT fk_completions_user FOREIGN KEY (UserId) REFERENCES Users(UserId),
    CONSTRAINT fk_completions_lesson FOREIGN KEY (LessonId) REFERENCES Lessons(LessonId),
    UNIQUE KEY uq_completion (UserId, LessonId)
);

-- Users (10) -----------------------------------------------------------
INSERT INTO Users (FullName, Email, Role) VALUES
    ('Ava Thompson', 'ava.thompson@example.com', 'learner'),
    ('Liam Carter', 'liam.carter@example.com', 'learner'),
    ('Noah Bennett', 'noah.bennett@example.com', 'learner'),
    ('Emma Walsh', 'emma.walsh@example.com', 'learner'),
    ('Olivia Grant', 'olivia.grant@example.com', 'instructor'),
    ('Mia Sullivan', 'mia.sullivan@example.com', 'learner'),
    ('Ethan Brooks', 'ethan.brooks@example.com', 'learner'),
    ('Sophia Reyes', 'sophia.reyes@example.com', 'admin'),
    ('Lucas Ferreira', 'lucas.ferreira@example.com', 'learner'),
    ('Grace Kim', 'grace.kim@example.com', 'learner');

-- Courses (8) ------------------------------------------------------------
INSERT INTO Courses (CourseName, Category, DurationHours, Instructor, Price, IsActive) VALUES
    ('Introduction to SQL', 'Data', 8.0, 'Alex Rivera', 49.00, 1),
    ('Advanced C# Patterns', 'Programming', 12.5, 'Jordan Lee', 89.00, 1),
    ('Cloud Architecture Fundamentals', 'Cloud', 10.0, 'Sam Patel', 79.00, 1),
    ('Docker & Containers 101', 'DevOps', 6.0, 'Morgan Diaz', 39.00, 1),
    ('Data Visualization with Power BI', 'Data', 9.0, 'Taylor Kim', 59.00, 1),
    ('Effective Communication Skills', 'Soft Skills', 4.0, 'Riley Chen', 29.00, 1),
    ('Workplace Safety Essentials', 'Compliance', 2.0, 'Casey Brooks', 19.00, 0),
    ('Project Management Basics', 'Management', 7.5, 'Jamie Fox', 69.00, 1);

-- Lessons (3 per course, 24 total) ---------------------------------------
INSERT INTO Lessons (CourseId, Title, SequenceNo, DurationMinutes) VALUES
    (1, 'Selecting Data', 1, 25), (1, 'Filtering & Sorting', 2, 30), (1, 'Joins & Aggregation', 3, 40),
    (2, 'Dependency Injection', 1, 35), (2, 'Async Patterns', 2, 40), (2, 'Design Patterns in Practice', 3, 45),
    (3, 'Compute & Storage Basics', 1, 30), (3, 'Networking in the Cloud', 2, 35), (3, 'Designing for Scale', 3, 40),
    (4, 'Images & Containers', 1, 20), (4, 'Docker Compose Basics', 2, 25), (4, 'Container Networking', 3, 30),
    (5, 'Connecting Data Sources', 1, 25), (5, 'Building Dashboards', 2, 35), (5, 'Sharing Reports', 3, 20),
    (6, 'Active Listening', 1, 15), (6, 'Written Communication', 2, 20), (6, 'Presenting with Confidence', 3, 25),
    (7, 'Hazard Identification', 1, 15), (7, 'Emergency Procedures', 2, 15), (7, 'Reporting Incidents', 3, 10),
    (8, 'Planning & Scoping', 1, 30), (8, 'Managing Stakeholders', 2, 25), (8, 'Tracking Progress', 3, 25);

-- Enrollments (21) --------------------------------------------------------
INSERT INTO Enrollments (UserId, CourseId, Status) VALUES
    (1, 1, 'completed'), (1, 2, 'active'), (1, 5, 'active'),
    (2, 1, 'completed'), (2, 3, 'completed'),
    (3, 2, 'active'), (3, 4, 'completed'),
    (4, 1, 'active'), (4, 6, 'completed'),
    (5, 3, 'completed'),
    (6, 4, 'active'), (6, 5, 'completed'), (6, 8, 'active'),
    (7, 1, 'dropped'), (7, 2, 'completed'),
    (8, 6, 'completed'), (8, 8, 'completed'),
    (9, 3, 'active'), (9, 4, 'active'),
    (10, 5, 'completed'), (10, 1, 'completed'), (10, 8, 'active');

-- Lesson completions (48), consistent with the enrollment statuses above ---
INSERT INTO LessonCompletions (UserId, LessonId, Score) VALUES
    -- User 1: Course 1 completed, Course 2 & 5 in progress
    (1, 1, 88.00), (1, 2, 92.00), (1, 3, 95.00), (1, 4, 75.00), (1, 13, 80.00),
    -- User 2: Course 1 & 3 completed
    (2, 1, 90.00), (2, 2, 85.00), (2, 3, 91.00), (2, 7, 77.00), (2, 8, 82.00), (2, 9, 88.00),
    -- User 3: Course 2 in progress, Course 4 completed
    (3, 4, 70.00), (3, 5, 74.00), (3, 10, 95.00), (3, 11, 93.00), (3, 12, 90.00),
    -- User 4: Course 1 in progress, Course 6 completed
    (4, 1, 60.00), (4, 16, 85.00), (4, 17, 89.00), (4, 18, 92.00),
    -- User 5: Course 3 completed
    (5, 7, 99.00), (5, 8, 97.00), (5, 9, 96.00),
    -- User 6: Course 4 & 8 in progress, Course 5 completed
    (6, 10, 72.00), (6, 13, 81.00), (6, 14, 79.00), (6, 15, 84.00), (6, 22, 68.00),
    -- User 7: Course 1 dropped (partial), Course 2 completed
    (7, 1, 55.00), (7, 4, 91.00), (7, 5, 88.00), (7, 6, 94.00),
    -- User 8: Course 6 & 8 completed
    (8, 16, 90.00), (8, 17, 92.00), (8, 18, 89.00), (8, 22, 87.00), (8, 23, 90.00), (8, 24, 85.00),
    -- User 9: Course 3 & 4 in progress
    (9, 7, 65.00), (9, 10, 70.00), (9, 11, 73.00),
    -- User 10: Course 5 & 1 completed, Course 8 in progress
    (10, 13, 93.00), (10, 14, 95.00), (10, 15, 91.00), (10, 1, 82.00), (10, 2, 85.00), (10, 3, 80.00), (10, 22, 77.00);
