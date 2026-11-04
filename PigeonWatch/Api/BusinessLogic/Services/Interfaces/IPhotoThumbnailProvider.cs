namespace PigeonWatch.BusinessLogic.Services;

public interface IPhotoThumbnailProvider
{
    byte[] CreateThumbnail(byte[] jpeg);
}
