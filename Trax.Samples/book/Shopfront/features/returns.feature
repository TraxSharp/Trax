Feature: Returning an order
  A customer sends an order back and says why. The shop checks the order is theirs before it takes
  the return.

  Background:
    Given Ada is a customer
    And Ada has a return

  Scenario: A return says which order and why
    When Ada fills in her return for order "3f2b8c1e-5d4a-4f7b-9c2e-1a6d8e0b7f45" because "It arrived broken"
    Then Ada's return is a draft
    And Ada's returns show reason "It arrived broken"

  Scenario: A return with a reason is sent to be checked
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    And Ada's returns show status "Being checked"

  Scenario: A return with no reason is refused
    Given Ada's return has no reason
    When Ada sends her return
    Then Ada is told "Say why you are sending it back."
    And Ada's return is still a draft

  # The tests start the hub alone, and the hub hands every job to the worker: a scenario that
  # waits for the check's answer runs in memory only.
  @in-memory
  Scenario: The shop accepts the return of Ada's own order
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return is accepted
    Then Ada's return is accepted
    And Ada's returns show status "Accepted"

  @in-memory
  Scenario: The shop refuses the return of an order that is not Ada's
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return is refused
    Then Ada's return was refused
    And Ada's returns show status "Refused"

  @in-memory
  Scenario: A check that fails leaves the return unchecked
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return cannot be checked
    Then Ada's return could not be checked

  @in-memory
  Scenario: A check called off leaves the return unchecked
    Given Ada's return has a reason
    When Ada sends her return
    Then Ada's return is being checked
    When the return is called off
    Then Ada's return could not be checked
    And Ada's returns show status "Not checked yet"
