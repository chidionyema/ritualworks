using System.Collections.Generic;
using System.Linq;

namespace haworks.Models
{
    public record FileValidationResult(
        bool IsValid,
        string FileType,
        IEnumerable<string> Errors)
    {
        public FileValidationResult() : this(false, "unknown", new List<string>()) { }

        public FileValidationResult AddError(string error)
        {
            var errors = Errors.ToList();
            errors.Add(error);
            return this with { Errors = errors };
        }
    }
}
