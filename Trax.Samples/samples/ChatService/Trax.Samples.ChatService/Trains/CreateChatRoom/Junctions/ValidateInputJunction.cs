using Trax.Core.Exceptions;
using Trax.Core.Junction;

namespace Trax.Samples.ChatService.Trains.CreateChatRoom.Junctions;

public class ValidateInputJunction : Junction<CreateChatRoomInput, CreateChatRoomInput>
{
    public override Task<CreateChatRoomInput> Run(CreateChatRoomInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new TrainException("Chat room name is required.");

        return Task.FromResult(input);
    }
}
