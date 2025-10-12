namespace Dotsesses.Messages;

/// <summary>
/// Message sent when a student should be edited.
/// </summary>
public class EditStudentMessage
{
    public int StudentId { get; }

    public EditStudentMessage(int studentId)
    {
        StudentId = studentId;
    }
}

/// <summary>
/// Message sent after a student has been edited.
/// </summary>
public class StudentEditedMessage
{
    public int StudentId { get; }

    public StudentEditedMessage(int studentId)
    {
        StudentId = studentId;
    }
}
