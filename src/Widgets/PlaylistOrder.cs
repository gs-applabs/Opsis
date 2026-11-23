namespace MotionDesk.Widgets
{
    /// <summary>Pure reordering of the video playlist (drag and drop in the wallpaper library, plan 2.1.9).</summary>
    public static class PlaylistOrder
    {
        /// <summary>Moves the item at <paramref name="from"/> so that it ends up before the ORIGINAL item at <paramref name="insertBefore"/> (insertBefore == Count means the end).</summary>
        public static bool Move<T>(IList<T> list, int from, int insertBefore)
        {
            if (from < 0 || from >= list.Count) return false;
            insertBefore = Math.Clamp(insertBefore, 0, list.Count);
            if (insertBefore == from || insertBefore == from + 1) return false;      // same place
            var item = list[from]; list.RemoveAt(from);
            list.Insert(insertBefore > from ? insertBefore - 1 : insertBefore, item);
            return true;
        }
    }
}
