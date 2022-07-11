using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using AWS.DistributedCacheProvider;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Sdk;

namespace AWS.DistributedCacheProviderIntegrationTests
{
    public class DynamoDBDistributedCacheTableTests
    {
        private readonly static string TABLE_NAME_PREFIX = "AWS.DistributedCacheProviderIntegrationTests_";

        /// <summary>
        /// Tests that our factory will create a Table if it does not already exist.
        /// </summary>
        [Fact]
        public async void CreateTable()
        {
            var tableName = TABLE_NAME_PREFIX + "table_test_0";
            var client = new AmazonDynamoDBClient();
            try
            {
                //First verify that a table with this name does not already exist.
                try
                {
                    _ = await client.DescribeTableAsync(tableName);
                    //If no exception was thrown, then the table already exists, bad state for test.
                    throw new XunitException("Table already exists, cannot create table");
                }
                catch (ResourceNotFoundException) { }
                var cache = GetCache(options =>
                {
                    options.TableName = tableName;
                    options.CreateTableIfNotExists = true;
                });
                //With lazy implementation, table creation is delayed until the client actually needs it.
                //resolving the table should pass. We then have a NotImplementedException until we implement CRUD operations.
                Assert.Throws<NotImplementedException>(() => cache.Get(""));
            }
            finally
            {
                //Delete DynamoDB table
                CleanupTable(client, tableName);
            }
        }

        /// <summary>
        /// Test that our Cache can load a table that already exists
        /// </summary>
        [Fact]
        public async void LoadValidTableTest()
        {
            var key = "key";
            var tableName = TABLE_NAME_PREFIX + "table_test_1";
            var client = new AmazonDynamoDBClient();
            //Valid table - Non-composite Hash key of type String.
            var request = new CreateTableRequest
            {
                TableName = tableName,
                KeySchema = new List<KeySchemaElement>
            {
                new KeySchemaElement
                {
                    AttributeName = key,
                    KeyType = KeyType.HASH
                }
            },
                AttributeDefinitions = new List<AttributeDefinition>
            {
                new AttributeDefinition
                {
                    AttributeName = key,
                    AttributeType = ScalarAttributeType.S
                }
            },
                BillingMode = BillingMode.PAY_PER_REQUEST
            };
            try { 
                //create the table here.
                await CreateAndWaitUntilActive(client, request);
                var cache = GetCache(options =>
                {
                    options.TableName = tableName;
                    options.CreateTableIfNotExists = false;
                });
                //With lazy implementation, table creation is delayed until the client actually needs it.
                //resolving the table should pass. We then have a NotImplementedException until we implement CRUD operations.
                Assert.Throws<NotImplementedException>(() => cache.Get(""));
            }
            finally
            {
                CleanupTable(client, tableName);
            }
        }

        /// <summary>
        /// Tests that our cache can reject a table if it is invalid. Invalid becuase Key is non-composite
        /// </summary>
        [Fact]
        public async void LoadInvalidTable_TooManyKeysTest()
        {
            var key1 = "key";
            var key2 = "key2";
            var tableName = TABLE_NAME_PREFIX + "table_test_2";
            var client = new AmazonDynamoDBClient();
            var request = new CreateTableRequest
            //Invalid becuase key is non-composite
            {
                TableName = tableName,
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement
                    {
                        AttributeName = key1,
                        KeyType = KeyType.HASH
                    },
                    new KeySchemaElement
                    {
                        AttributeName = key2,
                        KeyType = KeyType.RANGE
                    }
                },
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition
                    {
                        AttributeName = key1,
                        AttributeType = ScalarAttributeType.S
                    },
                    new AttributeDefinition
                    {
                        AttributeName = key2,
                        AttributeType = ScalarAttributeType.N
                    }
                },
                BillingMode = BillingMode.PAY_PER_REQUEST
            };
            try
            {
                //Create table here
                await CreateAndWaitUntilActive(client, request);
                var cache = GetCache(options =>
                {
                    options.TableName = tableName;
                    options.CreateTableIfNotExists = false;
                });
                //With lazy implementation, table creation is delayed until the client actually needs it.
                //resolving the table should not pass as the key is invalid.
                Assert.Throws<AmazonDynamoDBException>(() => cache.Get(""));
            }
            finally
            {
                CleanupTable(client, tableName);
            }
        }

        /// <summary>
        /// Tests that our cache can reject a table if it is invalid. Invalid becuase Key is not a String
        /// </summary>
        [Fact]
        public async void LoadInvalidTable_BadKeyTypeTest()
        {
            var key = "key";
            var tableName = TABLE_NAME_PREFIX + "table_test_3";
            var client = new AmazonDynamoDBClient();
            var request = new CreateTableRequest
            {
                TableName = tableName,
                KeySchema = new List<KeySchemaElement>
                {
                    new KeySchemaElement
                    {
                        AttributeName = key,
                        KeyType = KeyType.HASH
                    },
                },
                AttributeDefinitions = new List<AttributeDefinition>
                {
                    new AttributeDefinition
                    {
                        AttributeName = key,
                        AttributeType = ScalarAttributeType.N
                    }
                },
                BillingMode = BillingMode.PAY_PER_REQUEST
            };
            try
            {
                await CreateAndWaitUntilActive(client, request);
                var cache = GetCache(options =>
                {
                    options.TableName = tableName;
                    options.CreateTableIfNotExists = false;
                });
                //With lazy implementation, table creation is delayed until the client actually needs it.
                //resolving the table should not pass as the key is invalid.
                Assert.Throws<AmazonDynamoDBException>(() => cache.Get(""));
            }
            finally
            {
                CleanupTable(client, tableName);
            }
        }

        private async Task CreateAndWaitUntilActive(AmazonDynamoDBClient client, CreateTableRequest request)
        {
            await client.CreateTableAsync(request);
            await WaitUntilActive(client, request.TableName);

        }

        private async Task WaitUntilActive(AmazonDynamoDBClient client, string tableName)
        {
            var isActive = false;
            while (!isActive)
            {
                var descRequest = new DescribeTableRequest
                {
                    TableName = tableName
                };
                var descResponse = await client.DescribeTableAsync(descRequest);
                var tableStatus = descResponse.Table.TableStatus;

                if (tableStatus == TableStatus.ACTIVE)
                    isActive = true;
            }
        }

        private async void CleanupTable(AmazonDynamoDBClient client, string tableName)
        {
            await WaitUntilActive(client, tableName);
            client.DeleteTableAsync(tableName).Wait();
            var exists = true;
            while (exists)
            {
                var task = client.ListTablesAsync();
                var resp = task.Result;
                if (!resp.TableNames.Contains(tableName))
                {
                    exists = false;
                }
            }
        }

        private DynamoDBDistributedCache GetCache(Action<DynamoDBDistributedCacheOptions> options)
        {
            var serviceContainer = new ServiceCollection();
            serviceContainer.AddAWSDynamoDBDistributedCache(options);
            var provider = ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(serviceContainer);
            return (DynamoDBDistributedCache)provider.GetService<IDistributedCache>();
        }
    }
}
