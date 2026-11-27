using TrakHound.Example.MqttToTimeSeries;

namespace TrakHound.Example.MqttToTimeSeries.Tests
{
    public class TopicMappingTests
    {
        [Fact]
        public void Pattern_SubscribesWithWildcards()
        {
            var mapping = TopicMapping.Parse("plant/{line}/{machine}/{field}");

            Assert.Equal("plant/+/+/+", mapping.SubscribeTopic);
            Assert.Equal(new[] { "line", "machine" }, mapping.TagNames);
        }

        [Fact]
        public void Topic_MapsToTagsAndField()
        {
            var mapping = TopicMapping.Parse("plant/{line}/{machine}/{field}");

            Assert.True(mapping.TryMap("plant/line1/press-1/temperature", out var tags, out var field));
            Assert.Equal("line1", tags["line"]);
            Assert.Equal("press-1", tags["machine"]);
            Assert.Equal("temperature", field);
        }

        [Theory]
        [InlineData("plant/line1/press-1")]               // Too few levels
        [InlineData("plant/line1/press-1/temperature/x")] // Too many levels
        [InlineData("factory/line1/press-1/temperature")] // A different first level
        [InlineData("plant//press-1/temperature")]        // An empty Tag value
        [InlineData("")]
        public void Topic_NotMatching_IsSkipped(string topic)
        {
            var mapping = TopicMapping.Parse("plant/{line}/{machine}/{field}");

            Assert.False(mapping.TryMap(topic, out _, out _));
        }

        [Fact]
        public void MultiLevelWildcard_MatchesDeeperTopics()
        {
            var mapping = TopicMapping.Parse("sensors/{device}/#");

            Assert.Equal("sensors/+/#", mapping.SubscribeTopic);
            Assert.True(mapping.TryMap("sensors/s1/a/b", out var tags, out var field));
            Assert.Equal("s1", tags["device"]);
            Assert.Equal(TopicMapping.DefaultField, field);
            Assert.True(mapping.TryMap("sensors/s1", out _, out _));
        }

        [Fact]
        public void SingleLevelWildcard_IsNotATag()
        {
            var mapping = TopicMapping.Parse("plant/+/{machine}/{field}");

            Assert.Equal("plant/+/+/+", mapping.SubscribeTopic);
            Assert.True(mapping.TryMap("plant/anything/m1/speed", out var tags, out _));
            Assert.Single(tags);
        }

        [Theory]
        [InlineData("")]
        [InlineData("plant/#/{machine}")]           // '#' not last
        [InlineData("plant/{machine}/{machine}")]   // A Tag twice
        [InlineData("plant/{field}/{field}")]       // {field} twice
        [InlineData("plant/{1st}")]                 // Not a valid name
        [InlineData("plant/{time}")]                // A reserved name
        [InlineData("plant/line+")]                 // A wildcard inside a level
        [InlineData("#")]                           // No levels before '#'
        public void InvalidPattern_Throws(string pattern)
        {
            Assert.Throws<ArgumentException>(() => TopicMapping.Parse(pattern));
        }

        [Theory]
        [InlineData("temperature", "temperature")]
        [InlineData("spindle-speed", "spindle_speed")]
        [InlineData("1st", "_1st")]
        [InlineData("time", "time_value")]
        [InlineData("Quality", "Quality_value")]
        public void FieldName_IsValid(string level, string expected)
        {
            Assert.Equal(expected, TopicMapping.ToFieldName(level));
        }

        [Fact]
        public void FieldName_IsShortened()
        {
            Assert.Equal(60, TopicMapping.ToFieldName(new string('a', 80)).Length);
        }
    }
}
