using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PantmigService.Entities;
using PantmigService.Services;
using PantmigService.Endpoints.Helpers;
using PantmigService.Security;
using Xunit;

namespace PantMigTesting.Services;

public class ValidationServicesTests
{
    private readonly IRecycleListingValidationService _listingValidator = new RecycleListingValidationService();
    private readonly IFileValidationService _fileValidator;
    private readonly IChatValidationService _chatValidator = new ChatValidationService();

    public ValidationServicesTests()
    {
        _fileValidator = new FileValidationService(_listingValidator);
    }

    [Fact]
    public void ValidateCreate_Fails_When_TitleMissing()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from.AddDays(1);
        var res = _listingValidator.ValidateCreate(null, "desc", "City", null, from, to, null, null, new());
        Assert.False(res.IsValid);
        Assert.Equal("Validation error", res.Problem!.Title);
    }

    [Fact]
    public void ValidateCreate_Fails_When_Dates_Invalid()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from; // same day invalid per rule (must be after)
        var res = _listingValidator.ValidateCreate("Title", "Desc", "City", null, from, to, null, null, new());
        Assert.False(res.IsValid);
        Assert.Contains("AvailableTo", res.Problem!.Detail);
    }

    [Fact]
    public void ValidateCreate_Fails_When_No_Items()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from.AddDays(1);
        var res = _listingValidator.ValidateCreate("T", "D", "City", null, from, to, null, null, new());
        Assert.False(res.IsValid);
        Assert.Contains("At least one item", res.Problem!.Detail);
    }

    [Fact]
    public void ValidateCreate_Fails_When_Item_Quantity_Invalid()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from.AddDays(1);
        var items = new List<CreateListingItemInput> { new(RecycleMaterialType.Can, 0, null, null) };
        var res = _listingValidator.ValidateCreate("T", "D", "City", null, from, to, null, null, items);
        Assert.False(res.IsValid);
        Assert.Contains("greater than", res.Problem!.Detail);
    }

    [Fact]
    public void ValidateCreate_Fails_When_Item_Quantity_Too_Large()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from.AddDays(1);
        var items = new List<CreateListingItemInput> { new(RecycleMaterialType.Can, 10_001, null, null) };
        var res = _listingValidator.ValidateCreate("T", "D", "City", null, from, to, null, null, items);
        Assert.False(res.IsValid);
        Assert.Contains("too large", res.Problem!.Detail);
    }

    [Fact]
    public void ValidateCreate_Computes_Estimated_Value()
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var to = from.AddDays(1);
        var items = new List<CreateListingItemInput>
        {
            new(RecycleMaterialType.Can, 10, null, 0.5m),
            new(RecycleMaterialType.PlasticBottle, 5, null, null),
            new(RecycleMaterialType.GlassBottle, 2, null, 1.25m)
        };
        var res = _listingValidator.ValidateCreate("T", "D", "City", null, from, to, null, null, items);
        Assert.True(res.IsValid);
        // 10*0.5 + 2*1.25 = 5 + 2.5 = 7.5
        Assert.Equal(7.5m, res.Value!.EstimatedValue);
    }

    [Fact]
    public void FileValidation_Fails_When_Null_Or_Empty()
    {
        var r1 = _fileValidator.ValidateImage(null);
        Assert.False(r1.IsValid);
        var empty = new FormFile(Stream.Null, 0, 0, "file", "a.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
        var r2 = _fileValidator.ValidateImage(empty);
        Assert.False(r2.IsValid); // length == 0
    }

    [Fact]
    public void FileValidation_Fails_When_Not_Image()
    {
        var bytes = Encoding.UTF8.GetBytes("hello");
        using var ms = new MemoryStream(bytes);
        var file = new FormFile(ms, 0, bytes.Length, "file", "note.txt") { Headers = new HeaderDictionary(), ContentType = "text/plain" };
        var res = _fileValidator.ValidateImage(file);
        Assert.False(res.IsValid);
    }

    [Fact]
    public void FileValidation_Succeeds_For_Image()
    {
        var bytes = new byte[] { 1,2,3 };
        using var ms = new MemoryStream(bytes);
        var file = new FormFile(ms, 0, bytes.Length, "file", "img.jpg") { Headers = new HeaderDictionary(), ContentType = "image/jpeg" };
        var res = _fileValidator.ValidateImage(file);
        Assert.True(res.IsValid);
    }

    [Fact]
    public void ChatValidation_Fails_When_Not_Accepted()
    {
        var listing = new RecycleListing { Id = 1, CreatedByUserId = "donator", Status = ListingStatus.Created, IsActive = true };
        var res = _chatValidator.ValidateStartChat(listing, "donator");
        Assert.False(res.IsValid);
        Assert.Contains("accepted state", res.Problem!.Detail);
    }

    [Fact]
    public void ChatValidation_Fails_When_No_Recycler()
    {
        var listing = new RecycleListing { Id = 1, CreatedByUserId = "donator", Status = ListingStatus.Accepted, IsActive = true };
        var res = _chatValidator.ValidateStartChat(listing, "donator");
        Assert.False(res.IsValid);
        Assert.Contains("No recycler", res.Problem!.Detail);
    }

    [Fact]
    public void ChatValidation_Fails_When_User_Not_Participant()
    {
        var listing = new RecycleListing { Id = 1, CreatedByUserId = "donator", AssignedRecyclerUserId = "recycler", Status = ListingStatus.Accepted, IsActive = true };
        var res = _chatValidator.ValidateStartChat(listing, "other");
        Assert.False(res.IsValid);
        Assert.Equal(StatusCodes.Status403Forbidden, res.Problem!.StatusCode);
    }

    [Fact]
    public void ChatValidation_Succeeds()
    {
        var listing = new RecycleListing { Id = 42, CreatedByUserId = "donator", AssignedRecyclerUserId = "recycler", Status = ListingStatus.Accepted, IsActive = true };
        var res = _chatValidator.ValidateStartChat(listing, "recycler");
        Assert.True(res.IsValid);
        Assert.Equal("listing-42", res.Value);
    }

    [Fact]
    public async Task CreateListingRequestParser_Parses_Json_Success()
    {
        var parser = new CreateListingRequestParser(_listingValidator, new NoOpAntivirusScanner(), NullLogger<CreateListingRequestParser>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            Title = "T",
            Description = "D",
            City = "C",
            AvailableFrom = DateOnly.FromDateTime(DateTime.UtcNow.Date),
            AvailableTo = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)),
            Items = new[] { new { Type = (int)RecycleMaterialType.Can, Quantity = 1 } }
        }));
        ctx.Request.ContentType = "application/json";

        var result = await parser.ParseAsync(ctx.Request, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("T", result.Title);
        Assert.Single(result.RawItems!);
    }

    [Fact]
    public async Task CreateListingRequestParser_Invalid_Json()
    {
        var parser = new CreateListingRequestParser(_listingValidator, new NoOpAntivirusScanner(), NullLogger<CreateListingRequestParser>.Instance);
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{ bad json"));
        ctx.Request.ContentType = "application/json";

        var result = await parser.ParseAsync(ctx.Request, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Contains("Invalid JSON", result.Problem!.Detail);
    }

    [Fact]
    public async Task CreateListingRequestParser_Multipart_NonImage_Fails()
    {
        var parser = new CreateListingRequestParser(_listingValidator, new NoOpAntivirusScanner(), NullLogger<CreateListingRequestParser>.Instance);

        var boundary = "----TestBoundary";
        var formContent = new MultipartFormDataContent(boundary);
        formContent.Add(new StringContent("T"), "Title");
        formContent.Add(new StringContent("D"), "Description");
        formContent.Add(new StringContent("C"), "City");
        formContent.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow.Date).ToString("yyyy-MM-dd")), "AvailableFrom");
        formContent.Add(new StringContent(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)).ToString("yyyy-MM-dd")), "AvailableTo");
        formContent.Add(new StringContent("[{\"Type\":3,\"Quantity\":1}]"), "Items");
        var bytes = Encoding.UTF8.GetBytes("hello");
        formContent.Add(new ByteArrayContent(bytes){ Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain") } }, "images", "note.txt");

        var stream = new MemoryStream();
        await formContent.CopyToAsync(stream);
        stream.Position = 0;

        var ctx = new DefaultHttpContext();
        ctx.Request.ContentType = "multipart/form-data; boundary=" + boundary;
        ctx.Request.Body = stream;

        var result = await parser.ParseAsync(ctx.Request, CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Contains("must be images", result.Problem!.Detail);
    }
}
