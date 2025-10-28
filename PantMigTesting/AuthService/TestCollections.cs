using Xunit;

namespace PantMigTesting.AuthServiceTests;

// Disable parallelization for tests sharing the in-memory Auth test server and email capture
[CollectionDefinition("AuthServiceSequential", DisableParallelization = true)]
public class AuthServiceSequentialCollection : ICollectionFixture<object>
{
}
