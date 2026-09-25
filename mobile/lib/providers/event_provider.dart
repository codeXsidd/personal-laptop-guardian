import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../models/event.dart';
import '../services/event_service.dart';
import 'auth_provider.dart';

final eventServiceProvider = Provider<EventService>((ref) {
  return EventService(ref.watch(supabaseClientProvider));
});

class EventFilter {
  final String deviceId;
  final String? eventType;
  final List<String>? eventTypes;
  final int limit;
  final int offset;

  const EventFilter({
    required this.deviceId,
    this.eventType,
    this.eventTypes,
    this.limit = 50,
    this.offset = 0,
  });

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is EventFilter &&
          deviceId == other.deviceId &&
          eventType == other.eventType &&
          _listEquals(eventTypes, other.eventTypes) &&
          limit == other.limit &&
          offset == other.offset;

  @override
  int get hashCode =>
      Object.hash(deviceId, eventType, Object.hashAll(eventTypes ?? []), limit, offset);

  static bool _listEquals(List<String>? a, List<String>? b) {
    if (identical(a, b)) return true;
    if (a == null || b == null) return a == b;
    if (a.length != b.length) return false;
    for (var i = 0; i < a.length; i++) {
      if (a[i] != b[i]) return false;
    }
    return true;
  }
}

final eventsProvider =
    FutureProvider.family<List<ActivityEvent>, EventFilter>((ref, filter) async {
  return ref.read(eventServiceProvider).getEvents(
        filter.deviceId,
        limit: filter.limit,
        offset: filter.offset,
        eventType: filter.eventType,
        eventTypes: filter.eventTypes,
      );
});

final recentEventsProvider =
    FutureProvider.family<List<ActivityEvent>, String>((ref, deviceId) async {
  return ref.read(eventServiceProvider).getEvents(deviceId, limit: 20);
});

final eventTypeCountsProvider =
    FutureProvider.family<Map<String, int>, String>((ref, deviceId) async {
  final now = DateTime.now().toUtc();
  final yesterday = now.subtract(const Duration(hours: 24));
  return ref
      .read(eventServiceProvider)
      .getEventTypeCounts(deviceId, after: yesterday);
});
