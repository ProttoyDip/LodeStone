using Lodestone.Application.DTOs.Student;

namespace Lodestone.Application.Interfaces;

public interface IStudentDashboardService
{
    Task<StudentDashboardDto?> GetAsync(string userId, CancellationToken cancellationToken = default);
}
