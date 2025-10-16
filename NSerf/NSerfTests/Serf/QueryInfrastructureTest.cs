// Copyright (c) BoolHak, Inc.
// SPDX-License-Identifier: MPL-2.0
// Ported from: github.com/hashicorp/serf/serf/query_test.go

using System.Threading.Tasks;
using FluentAssertions;
using NSerf.Serf;
using Xunit;

namespace NSerfTests.Serf;

/// <summary>
/// Tests for Query infrastructure including QueryParam, QueryResponse, and related methods.
/// Based on query_test.go
/// </summary>
public class QueryInfrastructureTest
{
    [Fact]
    public void DefaultQueryTimeout_ShouldCalculateCorrectly()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);

        // Act
        var timeout = serf.DefaultQueryTimeout();

        // Assert
        timeout.Should().BeGreaterThan(TimeSpan.Zero, "timeout should be positive");

        // TODO: Phase 9 - With actual memberlist, verify exact calculation:
        // timeout = GossipInterval * QueryTimeoutMult * log10(N+1)
    }

    [Fact]
    public void DefaultQueryParams_ShouldHaveCorrectDefaults()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "test-node",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);

        // Act
        var params_ = serf.DefaultQueryParams();

        // Assert
        params_.Should().NotBeNull();
        params_.FilterNodes.Should().BeNull("default has no node filters");
        params_.FilterTags.Should().BeNull("default has no tag filters");
        params_.RequestAck.Should().BeFalse("default does not request acks");
        params_.Timeout.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void QueryParam_EncodeFilters_ShouldEncodeNodeFilters()
    {
        // Arrange
        var queryParam = new QueryParam
        {
            FilterNodes = new[] { "foo", "bar" }
        };

        // Act
        var filters = queryParam.EncodeFilters();

        // Assert
        filters.Should().NotBeNull();
        filters.Should().HaveCount(1, "should have one node filter");
        filters[0].Should().NotBeEmpty();
        // First byte should be filterNodeType (0)
        filters[0][0].Should().Be(0);
    }

    [Fact]
    public void QueryParam_EncodeFilters_ShouldEncodeTagFilters()
    {
        // Arrange
        var queryParam = new QueryParam
        {
            FilterTags = new Dictionary<string, string>
            {
                { "role", "^web" },
                { "datacenter", "aws$" }
            }
        };

        // Act
        var filters = queryParam.EncodeFilters();

        // Assert
        filters.Should().NotBeNull();
        filters.Should().HaveCount(2, "should have two tag filters");

        foreach (var filter in filters)
        {
            filter.Should().NotBeEmpty();
            // First byte should be filterTagType (1)
            filter[0].Should().Be(1);
        }
    }

    [Fact]
    public void QueryParam_EncodeFilters_ShouldEncodeBothNodeAndTagFilters()
    {
        // Arrange
        var queryParam = new QueryParam
        {
            FilterNodes = new[] { "foo", "bar" },
            FilterTags = new Dictionary<string, string>
            {
                { "role", "^web" },
                { "datacenter", "aws$" }
            }
        };

        // Act
        var filters = queryParam.EncodeFilters();

        // Assert
        filters.Should().NotBeNull();
        filters.Should().HaveCount(3, "should have 1 node filter + 2 tag filters");

        // First filter should be node filter (type 0)
        filters[0][0].Should().Be(0);

        // Remaining should be tag filters (type 1)
        filters[1][0].Should().Be(1);
        filters[2][0].Should().Be(1);
    }

    [Fact]
    public void ShouldProcessQuery_WithMatchingNodeFilter_ShouldReturnTrue()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "zip",
            Tags = new Dictionary<string, string>
            {
                { "role", "webserver" },
                { "datacenter", "east-aws" }
            }
        };
        var serf = new NSerf.Serf.Serf(config);

        var queryParam = new QueryParam
        {
            FilterNodes = new[] { "foo", "bar", "zip" }
        };
        var filters = queryParam.EncodeFilters();

        // Act
        var result = serf.ShouldProcessQuery(filters);

        // Assert
        result.Should().BeTrue("node name matches filter");
    }

    [Fact]
    public void ShouldProcessQuery_WithoutMatchingNodeFilter_ShouldReturnFalse()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "zip",
            Tags = new Dictionary<string, string>()
        };
        var serf = new NSerf.Serf.Serf(config);

        var queryParam = new QueryParam
        {
            FilterNodes = new[] { "foo", "bar" } // "zip" not in list
        };
        var filters = queryParam.EncodeFilters();

        // Act
        var result = serf.ShouldProcessQuery(filters);

        // Assert
        result.Should().BeFalse("node name doesn't match filter");
    }

    [Fact]
    public void ShouldProcessQuery_WithMatchingTagFilter_ShouldReturnTrue()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "zip",
            Tags = new Dictionary<string, string>
            {
                { "role", "webserver" },
                { "datacenter", "east-aws" }
            }
        };
        var serf = new NSerf.Serf.Serf(config);

        var queryParam = new QueryParam
        {
            FilterTags = new Dictionary<string, string>
            {
                { "role", "^web" },
                { "datacenter", "aws$" }
            }
        };
        var filters = queryParam.EncodeFilters();

        // Act
        var result = serf.ShouldProcessQuery(filters);

        // Assert
        result.Should().BeTrue("tags match regex filters");
    }

    [Fact]
    public void ShouldProcessQuery_WithMissingTag_ShouldReturnFalse()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "zip",
            Tags = new Dictionary<string, string>
            {
                { "role", "webserver" }
            }
        };
        var serf = new NSerf.Serf.Serf(config);

        var queryParam = new QueryParam
        {
            FilterTags = new Dictionary<string, string>
            {
                { "other", "cool" } // Tag doesn't exist
            }
        };
        var filters = queryParam.EncodeFilters();

        // Act
        var result = serf.ShouldProcessQuery(filters);

        // Assert
        result.Should().BeFalse("tag doesn't exist on node");
    }

    [Fact]
    public void ShouldProcessQuery_WithNonMatchingTagValue_ShouldReturnFalse()
    {
        // Arrange
        var config = new Config
        {
            NodeName = "zip",
            Tags = new Dictionary<string, string>
            {
                { "role", "webserver" }
            }
        };
        var serf = new NSerf.Serf.Serf(config);

        var queryParam = new QueryParam
        {
            FilterTags = new Dictionary<string, string>
            {
                { "role", "db" } // Doesn't match "webserver"
            }
        };
        var filters = queryParam.EncodeFilters();

        // Act
        var result = serf.ShouldProcessQuery(filters);

        // Assert
        result.Should().BeFalse("tag value doesn't match filter");
    }

    [Fact]
    public void KRandomMembers_ShouldReturnUpToKMembers()
    {
        // Arrange
        var members = new List<Member>();
        for (int i = 0; i < 90; i++)
        {
            var status = (i % 3) switch
            {
                0 => MemberStatus.Alive,
                1 => MemberStatus.Failed,
                _ => MemberStatus.Left
            };

            members.Add(new Member
            {
                Name = $"test{i}",
                Status = status
            });
        }

        Func<Member, bool> filterFunc = m => m.Name == "test0" || m.Status != MemberStatus.Alive;

        // Act
        var result1 = QueryHelpers.KRandomMembers(3, members, filterFunc);
        var result2 = QueryHelpers.KRandomMembers(3, members, filterFunc);
        var result3 = QueryHelpers.KRandomMembers(3, members, filterFunc);

        // Assert
        result1.Should().HaveCount(3, "should return 3 members");
        result2.Should().HaveCount(3);
        result3.Should().HaveCount(3);

        // Should be random (not always the same)
        var allSame = result1.SequenceEqual(result2) && result2.SequenceEqual(result3);
        allSame.Should().BeFalse("random selection should produce different results");

        // All should be StatusAlive and not "test0"
        foreach (var result in new[] { result1, result2, result3 })
        {
            foreach (var member in result)
            {
                member.Name.Should().NotBe("test0", "filtered out by filterFunc");
                member.Status.Should().Be(MemberStatus.Alive, "filtered out non-alive");
            }
        }
    }

    [Fact]
    public async Task QueryResponse_ShouldTrackResponses()
    {
        // Arrange
        var queryResponse = new QueryResponse(10, requestAck: false);

        var nodeResponse = new NodeResponse
        {
            From = "node1",
            Payload = new byte[] { 1, 2, 3 }
        };

        // Act
        await queryResponse.SendResponse(nodeResponse);

        // Assert
        queryResponse.ResponseCh.Should().NotBeNull();

        // TODO: Phase 9 - Add integration tests with actual channel reading
    }

    [Fact]
    public void QueryResponse_Close_ShouldCloseChannels()
    {
        // Arrange
        var queryResponse = new QueryResponse(10, requestAck: true);

        // Act
        queryResponse.Close();

        // Assert
        queryResponse.Finished().Should().BeTrue("should be marked as finished");
    }

    [Fact]
    public void QueryResponse_Finished_ShouldReturnTrueAfterDeadline()
    {
        // Arrange
        var deadline = DateTime.UtcNow.AddMilliseconds(-100); // Past deadline
        var queryResponse = new QueryResponse(10, requestAck: false)
        {
            Deadline = deadline
        };

        // Act
        var result = queryResponse.Finished();

        // Assert
        result.Should().BeTrue("past deadline means finished");
    }
}
