Feature: The gate
  A gate agent opening their desk opens check-in for the flight, and closing the gate there closes it. The flight
  departs four seconds after its gate closes, and the next one is scheduled ten seconds after that.

  Scenario: A gate agent opens check-in for everyone booked
    Given Ada is a passenger
    And Ada has a booking
    And Kit is a gate agent
    And Kit has a desk
    And the flight is scheduled
    When Kit opens his desk
    Then Kit's desk is open
    And the flight is open for check-in
    And Ada's booking is open for check-in
    And Kit's departures show status "Check-in open"

  Scenario: A gate agent closes the gate
    Given Ada is a passenger
    And Ada has a booking
    And Ada's booking is open for check-in
    And Kit is a gate agent
    And Kit has a desk
    And Kit's desk is open
    And the flight is open for check-in
    When Kit closes the gate at his desk
    Then Kit's desk is closing the gate
    And the flight is closed
    And Ada's booking missed the flight
    And Kit's departures show status "Gate closed"

  Scenario: A gate agent steps away once the gate is closed
    Given Kit is a gate agent
    And Kit has a desk
    And Kit's desk is closing the gate
    And the flight is closed
    When 2 seconds pass
    Then Kit's desk is done
    And the flight is still closed

  Scenario: The flight departs four seconds after its gate closes
    Given Kit is a gate agent
    And the flight is closed
    When 4 seconds pass
    Then the flight has departed
    And Kit's departures show status "Departed"

  Scenario: The next flight is scheduled ten seconds after one departs
    Given Ada is a passenger
    And the flight has departed
    When 10 seconds pass
    Then the flight is scheduled
    And Ada's departures show status "Scheduled"

  Scenario: Opening check-in on an open flight changes nothing
    Given the flight is open for check-in
    When check-in opens for the flight
    Then the flight is open for check-in

  Scenario: Closing the gate of a closed flight changes nothing
    Given the flight is closed
    When the flight closes its gate
    Then the flight is closed
