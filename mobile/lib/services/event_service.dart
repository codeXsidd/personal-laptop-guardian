import 'package:supabase_flutter/supabase_flutter.dart';

import '../models/event.dart';

class EventService {
  final SupabaseClient _client;

  EventService(this._client);

  Future<List<ActivityEvent>> getEvents(
    String deviceId, {
    int limit = 50,
    int offset = 0,
    String? eventType,
    List<String>? eventTypes,
    String? minSeverity,
    DateTime? after,
    DateTime? before,
  }) async {
    var query = _client
        .from('activity_events')
        .select()
        .eq('device_id', deviceId);

    if (eventType != null) {
      query = query.eq('event_type', eventType);
    } else if (eventTypes != null && eventTypes.isNotEmpty) {
      query = query.inFilter('event_type', eventTypes);
    }

    if (minSeverity != null) {
      query = query.inFilter('severity', _severitiesAtOrAbove(minSeverity));
    }

    if (after != null) {
      query = query.gte('timestamp', after.toIso8601String());
    }
    if (before != null) {
      query = query.lt('timestamp', before.toIso8601String());
    }

    final data = await query
        .order('timestamp', ascending: false)
        .range(offset, offset + limit - 1);

    return (data as List).map((e) => ActivityEvent.fromJson(e)).toList();
  }

  // Fetches event_type column and counts client-side.
  // Could be optimized with a server-side RPC for large datasets,
  // but already minimizes data transfer by selecting only the needed column.
  Future<Map<String, int>> getEventTypeCounts(
    String deviceId, {
    DateTime? after,
  }) async {
    var query = _client
        .from('activity_events')
        .select('event_type')
        .eq('device_id', deviceId);

    if (after != null) {
      query = query.gte('timestamp', after.toIso8601String());
    }

    final data = await query;
    final counts = <String, int>{};
    for (final row in data as List) {
      final type = row['event_type'] as String;
      counts[type] = (counts[type] ?? 0) + 1;
    }
    return counts;
  }

  List<String> _severitiesAtOrAbove(String severity) {
    const ordered = ['info', 'low', 'medium', 'high', 'critical'];
    final idx = ordered.indexOf(severity);
    if (idx < 0) return ordered;
    return ordered.sublist(idx);
  }
}
