using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Trax.StateMachine.Client;

// A predictor that cannot tell what every event leads to: one built from the IR, which holds no handler for a custom
// rule. The controller sends such an event with no prediction.
internal interface IPartialPredictor
{
    bool Predicts(string state, string trigger);
}
