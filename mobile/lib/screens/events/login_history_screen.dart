import 'package:flutter/material.dart';

import '../../models/event_type.dart';
import 'event_list_screen.dart';

class LoginHistoryScreen extends StatelessWidget {
  final String deviceId;

  const LoginHistoryScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    return EventListScreen(
      deviceId: deviceId,
      filterEventTypes: EventTypes.sessionTypes,
      title: 'Login / Session History',
    );
  }
}
