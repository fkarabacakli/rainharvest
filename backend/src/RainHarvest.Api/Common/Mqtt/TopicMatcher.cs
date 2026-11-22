namespace RainHarvest.Api.Common.Mqtt;

/// <summary>MQTT topic filter matching (MQTT 3.1.1 §4.7) for <c>+</c> and <c>#</c>.</summary>
public static class TopicMatcher
{
    public static bool Matches(string filter, string topic)
    {
        var filterLevels = filter.Split('/');
        var topicLevels = topic.Split('/');

        for (var i = 0; i < filterLevels.Length; i++)
        {
            var level = filterLevels[i];

            if (level == "#")
            {
                // '#' must be last; it matches the parent level and any number of children.
                return i == filterLevels.Length - 1;
            }

            if (i >= topicLevels.Length)
            {
                return false;
            }

            if (level != "+" && level != topicLevels[i])
            {
                return false;
            }
        }

        return filterLevels.Length == topicLevels.Length;
    }
}
