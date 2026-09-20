import 'package:flutter/material.dart';

import '../../models/event_type.dart';
import 'event_list_screen.dart';

class NetworkHistoryScreen extends StatelessWidget {
  final String deviceId;

  const NetworkHistoryScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    return EventListScreen(
      deviceId: deviceId,
      filterEventTypes: EventTypes.networkTypes,
      title: 'Network History',
    );
  }
}
