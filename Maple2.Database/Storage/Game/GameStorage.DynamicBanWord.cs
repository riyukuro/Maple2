using Maple2.Database.Extensions;
using Maple2.Model.Game;
using Maple2.Model.Validators;

namespace Maple2.Database.Storage;

public partial class GameStorage {
    public partial class Request {
        // The live ban-word list (DynamicBanWordRules), by id.
        public IList<DynamicBanWord> GetDynamicBanWords() {
            return Context.DynamicBanWord
                .OrderBy(word => word.Id)
                .AsEnumerable()
                .Select<Model.DynamicBanWord, DynamicBanWord>(word => word)
                .ToList();
        }

        // Null when the word is refused (DynamicBanWordRules.Refusal) or not stored.
        public DynamicBanWord? AddDynamicBanWord(int type, string word, string replacement, long addedBy) {
            if (DynamicBanWordRules.Refusal(word, replacement, GetDynamicBanWords()) != null) {
                return null;
            }

            var model = new Model.DynamicBanWord {
                Type = type,
                Word = word,
                Replacement = replacement,
                AddedBy = addedBy,
                CreationTime = DateTime.Now,
            };
            Context.DynamicBanWord.Add(model);
            return Context.TrySaveChanges() ? model : null;
        }

        public bool RemoveDynamicBanWord(int id) {
            Model.DynamicBanWord? model = Context.DynamicBanWord.Find(id);
            if (model == null) {
                return false;
            }
            Context.DynamicBanWord.Remove(model);
            return Context.TrySaveChanges();
        }
    }
}
