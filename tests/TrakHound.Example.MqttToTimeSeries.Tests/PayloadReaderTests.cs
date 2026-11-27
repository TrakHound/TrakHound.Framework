using TrakHound.Example.MqttToTimeSeries;

namespace TrakHound.Example.MqttToTimeSeries.Tests
{
    public class PayloadReaderTests
    {
        [Theory]
        [InlineData("71.3", 71.3)]
        [InlineData(" 42 ", 42.0)]
        [InlineData("-1.5e3", -1500.0)]
        [InlineData("{\"value\": 71.3}", 71.3)]
        [InlineData("{\"value\": 8000}", 8000.0)]   // A whole number is still a double
        [InlineData("{\"value\": \"12.5\"}", 12.5)] // A number in a string
        public void Number_IsADouble(string payload, double expected)
        {
            Assert.True(PayloadReader.TryRead(payload, "value", out var value, out _));
            Assert.Equal(expected, Assert.IsType<double>(value));
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("False", false)]
        [InlineData("{\"value\": true}", true)]
        public void Bool_IsABool(string payload, bool expected)
        {
            Assert.True(PayloadReader.TryRead(payload, "value", out var value, out _));
            Assert.Equal(expected, Assert.IsType<bool>(value));
        }

        [Fact]
        public void ValuePath_ReadsNestedProperties()
        {
            Assert.True(PayloadReader.TryRead("{\"data\": {\"temperature\": 21.5}}", "data.temperature", out var value, out _));
            Assert.Equal(21.5, value);
        }

        [Theory]
        [InlineData("")]
        [InlineData("running")]                       // Text
        [InlineData("NaN")]                           // Not a finite number
        [InlineData("{\"other\": 1}")]                // No 'value' property
        [InlineData("{\"value\": \"running\"}")]      // Text in the property
        [InlineData("{\"value\": null}")]
        [InlineData("{\"value\": [1, 2]}")]
        [InlineData("{not json")]
        public void Unreadable_ReturnsTheReason(string payload)
        {
            Assert.False(PayloadReader.TryRead(payload, "value", out var value, out var error));
            Assert.Null(value);
            Assert.False(string.IsNullOrEmpty(error));
        }
    }
}
