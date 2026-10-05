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

  /// Delete a single event by ID. Returns true if deleted.
  Future<bool> deleteEvent(String eventId) async {
    await _client.from('activity_events').delete().eq('id', eventId);
    return true;
  }

  /// Delete multiple events by their IDs. Returns count deleted.
  Future<int> deleteEvents(List<String> eventIds) async {
    if (eventIds.isEmpty) return 0;
    await _client.from('activity_events').delete().inFilter('id', eventIds);
    return eventIds.length;
  }

  /// Delete events older than [cutoff] for a device.
  /// Protected event types (security/audit) are excluded unless [includeProtected] is true.
  Future<int> deleteEventsOlderThan(
    String deviceId,
    DateTime cutoff, {
    bool includeProtected = false,
  }) async {
    var query = _client
        .from('activity_events')
        .delete()
        .eq('device_id', deviceId)
        .lt('timestamp', cutoff.toIso8601String());

    if (!includeProtected) {
      final inList = '(${_protectedEventTypes.join(",")})';
      query = query.not('event_type', 'in', inList);
    }

    final result = await query.select('id');
    return (result as List).length;
  }

  static const _protectedEventTypes = [
    'session_login',
    'session_logout',
    'login_failed',
    'session_lock',
    'session_unlock',
    'system_startup',
    'system_shutdown',
    'agent_started',
  ];

  /// Check if an event type is a protected security/audit type.
  static bool isProtectedType(String eventType) =>
      _protectedEventTypes.contains(eventType);

  List<String> _severitiesAtOrAbove(String severity) {
    const ordered = ['info', 'low', 'medium', 'high', 'critical'];
    final idx = ordered.indexOf(severity);
    if (idx < 0) return ordered;
    return ordered.sublist(idx);
  }
}
