-- name: course_completion_summary
-- queue: default
-- db: mysql_demo
SELECT
    c.CourseId,
    c.CourseName,
    c.Category,
    COUNT(e.EnrollmentId) AS EnrolledCount,
    SUM(CASE WHEN e.Status = 'completed' THEN 1 ELSE 0 END) AS CompletedCount,
    ROUND(100.0 * SUM(CASE WHEN e.Status = 'completed' THEN 1 ELSE 0 END) / NULLIF(COUNT(e.EnrollmentId), 0), 1) AS CompletionRatePct
FROM Courses c
LEFT JOIN Enrollments e ON e.CourseId = c.CourseId
GROUP BY c.CourseId, c.CourseName, c.Category
ORDER BY CompletionRatePct DESC;

-- name: user_progress
-- queue: default
-- db: mysql_demo
SELECT
    u.UserId,
    u.FullName,
    u.Role,
    COUNT(DISTINCT e.EnrollmentId) AS CoursesEnrolled,
    COUNT(DISTINCT lc.CompletionId) AS LessonsCompleted,
    ROUND(AVG(lc.Score), 1) AS AvgScore
FROM Users u
LEFT JOIN Enrollments e ON e.UserId = u.UserId
LEFT JOIN LessonCompletions lc ON lc.UserId = u.UserId
GROUP BY u.UserId, u.FullName, u.Role
ORDER BY LessonsCompleted DESC;

-- name: lesson_engagement
-- queue: default
-- db: mysql_demo
SELECT
    l.LessonId,
    c.CourseName,
    l.Title AS LessonTitle,
    l.SequenceNo,
    COUNT(lc.CompletionId) AS TimesCompleted,
    ROUND(AVG(lc.Score), 1) AS AvgScore
FROM Lessons l
JOIN Courses c ON c.CourseId = l.CourseId
LEFT JOIN LessonCompletions lc ON lc.LessonId = l.LessonId
GROUP BY l.LessonId, c.CourseName, l.Title, l.SequenceNo
ORDER BY c.CourseName, l.SequenceNo;

-- name: top_learners
-- queue: default
-- db: mysql_demo
SELECT
    u.UserId,
    u.FullName,
    COUNT(lc.CompletionId) AS LessonsCompleted,
    ROUND(AVG(lc.Score), 1) AS AvgScore,
    (SELECT COUNT(*) FROM Enrollments e WHERE e.UserId = u.UserId AND e.Status = 'completed') AS CoursesCompleted
FROM Users u
JOIN LessonCompletions lc ON lc.UserId = u.UserId
GROUP BY u.UserId, u.FullName
HAVING COUNT(lc.CompletionId) >= 3
ORDER BY AvgScore DESC, LessonsCompleted DESC;

-- name: courses_by_category
-- queue: default
-- db: mysql_demo
SELECT CourseId, CourseName, Category, DurationHours, Instructor, Price
FROM Courses
WHERE Category = ?
ORDER BY CourseName;
