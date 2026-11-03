namespace PigeonWatch.BusinessObjects;

public class PhotoStorageUnavailableException(string message, Exception inner) : Exception(message, inner);
