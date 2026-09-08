using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Entities;

namespace AltomateHR.Api.Tests.Support;

// Shared by the overtime test classes so one fake defines what the repository
// does, rather than each file inventing its own.
internal sealed class FakeOvertimeRepository : IOvertimeRepository
{
    private readonly List<OvertimeRequest> _requests;

    public FakeOvertimeRepository(IEnumerable<OvertimeRequest> requests) => _requests = requests.ToList();

    public Task<List<OvertimeRequest>> GetAllAsync() => Task.FromResult(_requests.ToList());

    public Task<OvertimeRequest?> GetByIdAsync(string id) =>
        Task.FromResult(_requests.FirstOrDefault(r => r.Id == id));

    public Task<List<OvertimeRequest>> GetByEmployeeAsync(string employeeId) =>
        Task.FromResult(_requests.Where(r => r.EmployeeId == employeeId).ToList());

    public Task<OvertimeRequest?> GetByPhotoUrlAsync(string photoUrl) =>
        Task.FromResult(_requests.FirstOrDefault(r =>
            r.BeforePhotoUrl == photoUrl || r.AfterPhotoUrl == photoUrl));

    public Task<OvertimeRequest> AddAsync(OvertimeRequest request)
    {
        _requests.Add(request);
        return Task.FromResult(request);
    }

    // The fakes hold the same instances the tests assert on, so an in-place
    // mutation is already visible — nothing to write back.
    public Task UpdateAsync(OvertimeRequest request) => Task.CompletedTask;
}

// Neither the bulk-approve path nor the admin reads touch photo storage; they
// only read AfterPhotoUrl off the request. Anything calling through here is a
// bug the test should show.
internal sealed class UnusedPhotoStorage : IOvertimePhotoStorage
{
    public Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload) =>
        throw new NotImplementedException();

    public Task<OvertimePhotoFileResult?> GetAsync(string fileName) =>
        throw new NotImplementedException();

    public Task<bool> DeleteAsync(string fileName) => throw new NotImplementedException();
}
