using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

public class ValidateInputJunction(ILogger<ValidateInputJunction> logger)
    : Junction<CreateChatRoomInput, CreateChatRoomInput>
{
    public override Task<CreateChatRoomInput> Run(CreateChatRoomInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new ArgumentException("Chat room name is required.");

        if (string.IsNullOrWhiteSpace(input.UserId))
            throw new ArgumentException("User ID is required.");

        if (string.IsNullOrWhiteSpace(input.DisplayName))
            throw new ArgumentException("Display name is required.");

        logger.LogInformation(
            "Validated CreateChatRoom input: name={Name}, user={UserId}",
            input.Name,
            input.UserId
        );

        return Task.FromResult(input);
    }
}
