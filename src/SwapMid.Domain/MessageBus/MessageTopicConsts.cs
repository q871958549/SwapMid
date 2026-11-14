namespace SwapMid.MessageBus;

public static class MessageTopicConsts
{
    public const int MaxNameLength = 128;
    public const int MaxDescriptionLength = 512;
}

public static class MessageSubscriptionConsts
{
    public const int MaxNameLength = 128;
    public const int MaxEndpointUrlLength = 512;
    public const int MaxFilterRuleLength = 1024;
    public const int MaxExchangeNameLength = 256;
    public const int MaxRoutingKeyLength = 256;
    public const int MaxSecretLength = 256;
}

public static class MessageRecordConsts
{
    public const int MaxSubjectLength = 256;
    public const int MaxPayloadLength = 8000;
    public const int MaxErrorMessageLength = 2048;
}
