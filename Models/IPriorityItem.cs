namespace OsuMate.Models
{
  public interface IPriorityItem
  {
    int Id { get; }
    bool IsEnabled { get; set; }
  }
}
