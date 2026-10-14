// What the examples on sdk-reference/graphql-api/subscriptions.md take as given: the values the
// JWT example configures its schemes with, and the interceptor the replacement example registers.
global using static SubscriptionsPage;
using HotChocolate.AspNetCore;

public static class SubscriptionsPage
{
    public static string cognitoAuthority =>
        "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_example";

    public static byte[] internalKey => new byte[32];
}

public class MySocketInterceptor : DefaultSocketSessionInterceptor;
