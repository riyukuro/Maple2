using Maple2.Database.Context;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UgcResource = Maple2.Model.Game.UgcResource;

namespace Maple2.Database.Storage;

public partial class WebStorage {
    private readonly ILogger logger;
    private readonly DbContextOptions options;

    public WebStorage(DbContextOptions options, ILogger<WebStorage> logger) {
        this.options = options;
        this.logger = logger;
    }

    public Request Context() {
        // We use NoTracking by default since most requests are Read or Overwrite.
        // If we need tracking for modifying data, we can set it individually as needed.
        var context = new WebContext(options);
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        return new Request(context, logger);
    }

    public partial class Request(WebContext context, ILogger logger) : DatabaseRequest<WebContext>(context, logger) {

        // uploadUntil opens the resource's upload (the web server takes its files until then, once each); targetId is the
        // folder its files go under (the design's item, the banner, the guild).
        public UgcResource? CreateUgc(UgcType type, long ownerId, long targetId = 0, DateTime? uploadUntil = null) {
            var model = new Model.UgcResource {
                Type = type,
                OwnerId = ownerId,
                TargetId = targetId,
                UploadUntil = uploadUntil,
            };
            Context.UgcResource.Add(model);

            return Context.TrySaveChanges() ? model : null;
        }

        public bool SaveUgc(UgcResource ugc, long ownerId) {
            Model.UgcResource model = ugc;
            model.OwnerId = ownerId;
            Context.UgcResource.Update(model);

            return SaveChanges();
        }

        public bool UpdatePath(long id, string path) {
            Model.UgcResource? model = Context.UgcResource.Find(id);
            if (model == null) {
                return false;
            }

            model.Path = path;
            Context.UgcResource.Update(model);
            return SaveChanges();
        }

        public UgcResource? GetUgc(long id) {
            return Context.UgcResource.Find(id);
        }

        // Takes one file of an open upload: the resource's upload is still open at now and the part not taken yet. A
        // taken part is never taken again, so nothing replaces it; the window stays, so a retry of the same file inside
        // it can be answered with the stored one (WebController). The save checks the row's LastModified, so of two
        // requests that read the same row only one takes the part.
        public bool TakeUpload(long id, byte part, DateTime now) {
            Model.UgcResource? model = Context.UgcResource.Find(id);
            if (model?.UploadUntil == null || now > model.UploadUntil.Value || (model.UploadedParts & part) != 0) {
                return false;
            }

            model.UploadedParts |= part;
            Context.UgcResource.Update(model);
            return Context.TrySaveChanges();
        }

        // Undoes a TakeUpload whose file was not written: the part is free again for the client's next try.
        public bool GiveBackUpload(long id, byte part) {
            Model.UgcResource? model = Context.UgcResource.Find(id);
            if (model == null || (model.UploadedParts & part) == 0) {
                return false;
            }

            model.UploadedParts = (byte) (model.UploadedParts & ~part);
            Context.UgcResource.Update(model);
            return Context.TrySaveChanges();
        }
    }
}
