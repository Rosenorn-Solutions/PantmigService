using System;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using PantmigService.Entities;
using PantmigService.Endpoints;
using System.Linq;
using Microsoft.VSDiagnostics;
using Newtonsoft.Json;

[CPUUsageDiagnoser]
public class RecycleListingSerializationBenchmark
{
    private RecycleListing _listing = null !;
    private RecycleListingResponse _response = null !;
    [GlobalSetup]
    public void Setup()
    {
        // Create a listing with a moderately large receipt image bytes to simulate real payload
        var imgSize = 80 * 1024; //80 KB
        var bytes = new byte[imgSize];
        var rnd = new Random(42);
        rnd.NextBytes(bytes);
        _listing = new RecycleListing
        {
            Id = 123,
            Title = "Benchmark Listing",
            Description = "This listing is used for serialization benchmark",
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = "bench-user",
            IsActive = true,
            Status = ListingStatus.Completed,
            CityId = 1,
            ReceiptImageBytes = bytes,
            ReceiptImageContentType = "image/jpeg",
            ReceiptImageFileName = "receipt.jpg",
        };
        // Add a couple of items and images
        _listing.Items.Add(new RecycleListingItem { Id = 1, ListingId = 123, MaterialType = RecycleMaterialType.Can, Quantity = 10 });
        _listing.Images.Add(new RecycleListingImage { Id = 1, ListingId = 123, Data = new byte[1024], ContentType = "image/jpeg", FileName = "photo1.jpg", Order = 0 });
        _response = _listing.ToResponse();
    }

    [Benchmark]
    public string SerializeResponse_Newtonsoft()
    {
        return JsonConvert.SerializeObject(_response);
    }

    [Benchmark]
    public string SerializeResponse_SystemTextJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(_response);
    }
}