Feature: Checking in for a flight
  A passenger checks in once check-in opens, with a passport number and a seat, and gets a boarding pass. A
  passenger who has not checked in when the gate closes misses the flight.

  Background:
    Given Ada is a passenger
    And Ada has a booking

  Scenario: Check-in opens for everyone booked on the flight
    Given the flight is scheduled
    When check-in opens for the flight
    Then the flight is open for check-in
    And Ada's booking is open for check-in
    And Ada's departures show status "Check-in open"

  Scenario: A passenger checks in and gets a boarding pass
    Given Ada's booking is open for check-in
    And Ada's booking has a passport number
    When Ada checks in for her booking in seat "14C"
    Then Ada's booking is getting a boarding pass
    When the boarding pass is issued
    Then Ada's booking is checked in
    And Ada's bookings show seat "14C"

  Scenario: Checking in with no passport number is refused
    Given Ada's booking is open for check-in
    And Ada's booking has no passport number
    When Ada checks in for her booking in seat "14C"
    Then Ada is told "Add your passport number first."
    And Ada's booking is still open for check-in

  Scenario: An expired passport is turned down
    Given Ada's booking is open for check-in
    When Ada gives passport "X0000000" for her booking
    Then Ada's booking is open for check-in
    When Ada checks in for her booking in seat "14C"
    Then Ada's booking is getting a boarding pass
    When the passport is turned down
    Then Ada's booking was turned down
    And Ada's bookings show status "Passport not accepted"

  Scenario: The gate closes on a passenger who has not checked in
    Given Ada's booking is open for check-in
    And the flight is open for check-in
    When the flight closes its gate
    Then the flight is closed
    And Ada's booking missed the flight
    And Ada's bookings show status "Missed the flight"
    And Ada's departures show status "Gate closed"

  Scenario: A passenger gives a passport number
    Given Ada's booking is open for check-in
    When Ada gives passport "X1234567" for her booking
    Then Ada's booking is open for check-in

  Scenario: Checking in before check-in opens does nothing
    When Ada checks in for her booking in seat "14C"
    Then Ada's booking is still booked

  Scenario: A checked-in passenger waits for the flight
    Given Ada's booking is checked in
    When 10 seconds pass
    Then Ada's booking is still checked in

  Scenario: A booking opens for check-in
    When Ada's booking opens for check-in
    Then Ada's booking is open for check-in

  Scenario: A booking misses the flight
    Given Ada's booking is open for check-in
    When Ada's booking misses the flight
    Then Ada's booking missed the flight

  # The host's boarding pass train never fails or is called off by itself: these two run in memory only.
  @in-memory
  Scenario: A boarding pass that cannot be issued leaves the booking without one
    Given Ada's booking is open for check-in
    And Ada's booking has a passport number
    When Ada checks in for her booking in seat "14C"
    Then Ada's booking is getting a boarding pass
    When the boarding pass cannot be issued
    Then Ada's booking has no boarding pass

  @in-memory
  Scenario: A boarding pass called off leaves the booking without one
    Given Ada's booking is open for check-in
    And Ada's booking has a passport number
    When Ada checks in for her booking in seat "14C"
    Then Ada's booking is getting a boarding pass
    When issuing the boarding pass is called off
    Then Ada's booking has no boarding pass
    And Ada's bookings show status "No boarding pass yet"
