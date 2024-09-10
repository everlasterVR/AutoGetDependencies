using System.Collections.Generic;
using System.Linq;

namespace everlaster
{
    sealed class Bindings
    {
        public readonly Dictionary<string, string> namespaceDict;
        readonly List<JSONStorableAction> _bindActions;

        public Bindings(string @namespace, List<JSONStorableAction> bindActions)
        {
            namespaceDict = new Dictionary<string, string>
            {
                ["Namespace"] = @namespace,
            };
            _bindActions = bindActions;
        }

        public IEnumerable<object> GetActions() => _bindActions.Select(action => (object) action);
    }
}
