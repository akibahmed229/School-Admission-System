using System.Net;
using Xunit;
using static SchoolAdmission.Tests.TestApi;

namespace SchoolAdmission.Tests;

[Collection("db")]
public sealed class SeatHoldTests : IDisposable
{
    private readonly ApiFactory _factory;
    private readonly HttpClient _http;

    public SeatHoldTests(PostgresFixture db)
    {
        _factory = new ApiFactory(db.ConnectionString);
        _http = _factory.CreateClient();
    }

    public void Dispose()
    {
        _http.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Concurrent_holds_never_exceed_total_seats()
    {
        const int seats = 10;
        const int requests = 200;
        var classId = await CreateClassAsync(_http, seats);

        var responses = await Task.WhenAll(Enumerable.Range(1, requests).Select(i =>
            Task.Run(() => SendAsync(_http, HttpMethod.Post, $"/classes/{classId}/holds", Student(i)))));

        Assert.Equal(seats, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(requests - seats, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var cls = await GetClassAsync(_http, classId);
        Assert.Equal(seats, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(0, cls.GetProperty("availableSeats").GetInt32());
    }

    [Fact]
    public async Task Concurrent_holds_by_the_same_student_create_only_one_hold()
    {
        var classId = await CreateClassAsync(_http, 50);
        var student = Student(777);

        var responses = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ =>
            Task.Run(() => SendAsync(_http, HttpMethod.Post, $"/classes/{classId}/holds", student))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(29, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, (await GetClassAsync(_http, classId)).GetProperty("heldSeats").GetInt32());
    }

    [Fact]
    public async Task Confirming_the_same_hold_many_times_counts_the_seat_once()
    {
        var classId = await CreateClassAsync(_http, 5);
        var student = Student(1);

        var created = await SendAsync(_http, HttpMethod.Post, $"/classes/{classId}/holds", student);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var holdId = (await JsonAsync(created)).GetProperty("holdId").GetString()!;

        var confirms = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => SendAsync(_http, HttpMethod.Post, $"/holds/{holdId}/confirm", student))));

        Assert.All(confirms, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var cls = await GetClassAsync(_http, classId);
        Assert.Equal(1, cls.GetProperty("confirmedSeats").GetInt32());
        Assert.Equal(0, cls.GetProperty("heldSeats").GetInt32());
        Assert.Equal(4, cls.GetProperty("availableSeats").GetInt32());
    }
}
